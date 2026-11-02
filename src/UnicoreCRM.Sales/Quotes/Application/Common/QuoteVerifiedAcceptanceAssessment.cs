using System.Security.Cryptography;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

/// <summary>Read-only assessments. This type has no evidence write or acceptance mutation capability.</summary>
internal sealed class QuoteVerifiedAcceptanceAssessment(QuoteAuthorization authorization, IQuotesPersistence quotes,
    IQuoteDocumentPersistence documents, QuoteDocumentCommands documentAccess, IQuoteVerifiedAcceptancePolicyReader policies,
    IQuoteApprovalPolicyReader approvals, IWorkspaceTimeZoneReader zones, TimeProvider clock, IQuoteProofPolicyReader? proofPolicies = null,
    IQuoteProofScanPolicyReader? scanPolicies=null,IQuoteProofRuntimeAuthority? scanRuntime=null,
    IQuoteIdentityPolicyReader? identityPolicies=null,IQuoteProofPersistence? proofStorage=null,QuoteIdentityEvaluation? identityEvaluation=null)
{
    internal static readonly string[] MissingContracts = ["BLOCKED_CONTRACT_IDENTITY_AUTHORITY_SOURCE",
        "BLOCKED_CONTRACT_TRUSTED_SCANNER", "BLOCKED_CONTRACT_ACCEPTANCE_PROOF_STORAGE"];

    internal async Task<QuoteOperationResult<QuoteCustomerAcceptanceReadiness>> ReadAsync(string quoteId, string? documentId,
        QuoteRequestMetadata metadata, CancellationToken ct) => await AssessAsync(quoteId, documentId, null, null, metadata, ct);

    internal async Task<QuoteOperationResult<QuoteCustomerAcceptanceReadiness>> EvaluateAsync(string quoteId,
        EvaluateQuoteCustomerAcceptanceRequest request, QuoteCommandMetadata command, CancellationToken ct)
    {
        var fields = new Dictionary<string, string[]>();
        if (request.Intent is not ("VERIFY" or "ACCEPT")) fields["intent"] = ["Choose an assessment intent"];
        if (!QuotePricing.EntityId(request.EvidenceId) || !QuotePricing.EntityId(request.DocumentId)) fields["references"] = ["Valid references required"];
        if (request.QuoteRevision < 1 || !Hash(request.PdfSha256) || !Hash(request.CommercialContentFingerprint)) fields["binding"] = ["Valid document binding required"];
        if (command.ExpectedVersion is null) fields["If-Match"] = ["Resource version required"];
        if (fields.Count > 0) return Fail(QuoteErrors.Validation(fields));
        if (!QuotePricing.EntityId(quoteId)) return Fail(QuoteErrors.NotFound());
        // No mutation receipt exists: repeated assessments must reauthorize and re-evaluate current facts.
        await using var tx = await quotes.BeginAsync(ct);
        var access = await authorization.AuthorizeAsync(new(command.RequestId, command.CorrelationId), ct);
        if (!access.IsSuccess) return Fail(access.Error!);
        await quotes.LockAsync($"aggregate:{access.Value!.Trusted.WorkspaceId}:{quoteId}", ct);
        var result = await AssessAsync(quoteId, request.DocumentId, request, command.ExpectedVersion,
            new(command.RequestId, command.CorrelationId), ct);
        if (result.IsSuccess) await tx.CommitAsync(ct); // Commits the read audit only.
        return result;
    }
    private async Task<QuoteOperationResult<QuoteCustomerAcceptanceReadiness>> AssessAsync(string quoteId, string? documentId,
        EvaluateQuoteCustomerAcceptanceRequest? request, long? expectedVersion, QuoteRequestMetadata metadata, CancellationToken ct)
    {
        // Retained constructor compatibility for historical consumers; B1 is not an issuance prerequisite.
        _ = approvals;
        var grants = new List<QuoteAccess>();
        var requirements = new List<AccessRequirement> { QuoteCapabilities.Read, QuoteCapabilities.ReadAcceptanceEvidence };
        if (request is not null) requirements.Add(request.Intent == "VERIFY" ? QuoteCapabilities.VerifyAcceptanceEvidence : QuoteCapabilities.AcceptVerified);
        if (request?.Intent == "ACCEPT") requirements.Add(QuoteCapabilities.Update);
        foreach (var requirement in requirements)
        {
            var result = await authorization.AuthorizeAsync(metadata, ct, requirement);
            if (!result.IsSuccess) return Fail(result.Error!); grants.Add(result.Value!);
        }
        if (!QuotePricing.EntityId(quoteId)) return Fail(QuoteErrors.NotFound());
        var quote = await quotes.ReadQuoteAsync(grants[0].Trusted.WorkspaceId, quoteId, ct);
        if (quote is null) return Fail(QuoteErrors.NotFound());
        foreach (var grant in grants)
            if (await authorization.EnforceRecordAsync(grant, quote, "quoteAcceptanceAssessment", metadata, ct) is { } denied) return Fail(denied);
        var required = QuoteDocumentCommands.RenderFields.Concat(QuoteDeliveryCommands.ReadFields)
            .Concat(["status", "archivedAt", "customerDecisionHistory"]);
        if (required.Any(f => grants.Any(g => !g.Authorization.CanRead(f)))) return Fail(QuoteErrors.AccessDenied());
        if (expectedVersion is not null && quote.ResourceVersion != expectedVersion) return Fail(new("VERSION_CONFLICT", 412, "Resource version conflict"));
        var blockers = new List<string>(); var policy = policies.Read(quote.WorkspaceId);
        if (policy is null) blockers.Add("VERIFICATION_POLICY_MISSING_OR_INVALID");
        else if (!QuoteVerifiedAcceptanceRules.WithinValueLimit(policy, quote.Currency, quote.GrandTotalAmount)) blockers.Add("DOCUMENTARY_VALUE_LIMIT_OR_CURRENCY");
        if (quote.Status != "SENT" || quote.ArchivedAt is not null) blockers.Add("QUOTE_STATE_INVALID");
        if (!QuoteVerifiedAcceptanceRules.HasCurrentManualDelivery(quote.DeliveryHistoryJson, quote.QuoteRevision,
            QuoteApprovalAssessment.ContentFingerprint(quote))) blockers.Add("COMMERCIAL_DELIVERY_EVIDENCE_UNAVAILABLE");
        var validity = QuoteValidity.Evaluate(quote.ValidUntil, await zones.ReadTimeZoneAsync(quote.WorkspaceId, ct), clock.GetUtcNow());
        if (validity.State is "OVERDUE" or "TIMEZONE_UNAVAILABLE") blockers.Add(validity.State == "OVERDUE" ? "QUOTE_EXPIRED" : "WORKSPACE_TIMEZONE_UNAVAILABLE");
        if (request?.Intent == "VERIFY")
        {
            var creator = await quotes.ReadCreatorAsync(quote.WorkspaceId, quote.QuoteId, ct);
            if (creator is null) blockers.Add("CREATOR_EVIDENCE_UNAVAILABLE");
            else if (grants[0].Trusted.MemberId == creator || grants[0].Trusted.MemberId == quote.ApprovalRequestedBy)
                return Fail(new("ACCESS_DENIED", 403, "Independent verifier required", "SELF_VERIFICATION_FORBIDDEN"));
            // Recorder identity cannot be checked from a client claim: actual stored evidence owner is absent.
        }
        QuoteAcceptanceDocumentBinding? binding = null;
        if (documentId is null) blockers.Add("ISSUED_DOCUMENT_SELECTION_REQUIRED");
        else
        {
            var found = await BindingAsync(quote, documentId, grants, metadata, blockers, ct);
            if (!found.IsSuccess) return Fail(found.Error!); binding = found.Value!;
            if (binding.QuoteRevision != quote.QuoteRevision || binding.RootQuoteId != quote.RootQuoteId
                || binding.BuyerRef.Type != quote.BuyerType || binding.BuyerRef.Id != quote.BuyerId
                || binding.CommercialContentFingerprint != QuoteApprovalAssessment.ContentFingerprint(quote)) blockers.Add("DOCUMENT_COMMERCIAL_BINDING_STALE");
            if (request is not null && (request.QuoteRevision != binding.QuoteRevision
                || !string.Equals(request.PdfSha256, binding.PdfSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(request.CommercialContentFingerprint, binding.CommercialContentFingerprint, StringComparison.OrdinalIgnoreCase)))
                return Fail(new("QUOTE_ACCEPTANCE_BINDING_MISMATCH", 409, "Issued document binding mismatch"));
        }
        if (quote.SourceDealId is not null) blockers.Add("LINKED_ACCEPTANCE_BLOCKED_GATE_C");
        var storageAvailable = proofPolicies?.Read(quote.WorkspaceId) is not null;
        var scanPolicy=scanPolicies?.Read(quote.WorkspaceId);
        var scannerAvailable=scanPolicy is not null && scanRuntime is not null && QuoteProofScanCoordinator.Fresh(await scanRuntime.ReadSnapshotAsync(ct),scanPolicy,clock.GetUtcNow());
        blockers.AddRange(MissingContracts.Where(code => (code!="BLOCKED_CONTRACT_ACCEPTANCE_PROOF_STORAGE" || !storageAvailable)
            && (code!="BLOCKED_CONTRACT_TRUSTED_SCANNER" || !scannerAvailable)));
        QuoteIdentityAssessment? identityAssessment=null;
        // Optional component of the existing readiness assessment, never an alternative acceptance engine.
        if(binding is not null && identityPolicies?.Read(quote.WorkspaceId) is not null && proofStorage is not null && identityEvaluation is not null)
        {
            var identityCase=await proofStorage.DocumentCaseAsync(quote.WorkspaceId,quote.QuoteId,binding.DocumentId,ct);
            if(identityCase is not null)
            {
                var identity=await identityEvaluation.ViewAsync(quote,identityCase,metadata,ct);
                if(!identity.IsSuccess)return Fail(identity.Error!);
                identityAssessment=identity.Value!.Assessment;
            }
        }
        var ignored = new[] { "BLOCKED_CONTRACT_IDENTITY_AUTHORITY_SOURCE", "BLOCKED_CONTRACT_TRUSTED_SCANNER", "LINKED_ACCEPTANCE_BLOCKED_GATE_C", "DOCUMENTARY_VALUE_LIMIT_OR_CURRENCY" };
        var submit = await authorization.AuthorizeAsync(metadata, ct, QuoteCapabilities.SubmitAcceptanceEvidence);
        var collectionAllowed = storageAvailable && binding is not null && blockers.All(code => ignored.Contains(code, StringComparer.Ordinal))
            && submit.IsSuccess && required.All(f => submit.Value!.Authorization.CanRead(f))
            && await authorization.EnforceRecordAsync(submit.Value!, quote, "quoteAcceptanceProof", metadata, ct) is null;
        quotes.AddReadAudit(new("quoteAcceptanceAssessment", quote.WorkspaceId, grants[0].Trusted.MemberId, quote.QuoteId,
            metadata.RequestId, metadata.CorrelationId, quote.ResourceVersion, clock.GetUtcNow()));
        await quotes.SaveChangesAsync(ct);
        // No proof/evidence ID is invented or echoed as verified. No command record/outbox/lifecycle writes.
        return QuoteOperationResult<QuoteCustomerAcceptanceReadiness>.Success(new(quote.QuoteId, quote.ResourceVersion, quote.Status,
            "BLOCKED_CONTRACT", "NOT_VERIFIED", false, blockers.Distinct(StringComparer.Ordinal).ToArray())
            { IdentityAuthority = identityAssessment, ProofCollectionAllowed = collectionAllowed, PolicyVersion = policy?.Version, AuthorityFreshnessHours = policy?.AuthorityFreshnessHours, Binding = binding });
    }
    private async Task<QuoteOperationResult<QuoteAcceptanceDocumentBinding>> BindingAsync(Quote quote, string documentId,
        IReadOnlyList<QuoteAccess> grants, QuoteRequestMetadata metadata, List<string> blockers, CancellationToken ct)
    {
        if (!QuotePricing.EntityId(documentId)) return QuoteOperationResult<QuoteAcceptanceDocumentBinding>.Failure(QuoteErrors.NotFound());
        var d = await documents.FindAsync(quote.WorkspaceId, quote.QuoteId, documentId, ct);
        if (d is null) return QuoteOperationResult<QuoteAcceptanceDocumentBinding>.Failure(QuoteErrors.NotFound());
        if (!await documentAccess.CanReadStoredAsync(d, grants, metadata, ct)) return QuoteOperationResult<QuoteAcceptanceDocumentBinding>.Failure(QuoteErrors.AccessDenied());
        var bytes = await documents.ReadBytesAsync(quote.WorkspaceId, documentId, ct);
        if (bytes is null || bytes.Length != d.FileSize || Convert.ToHexString(SHA256.HashData(bytes)) != d.PdfSha256)
            return QuoteOperationResult<QuoteAcceptanceDocumentBinding>.Failure(new("INTEGRATION_UNAVAILABLE", 503, "Issued document integrity unavailable"));
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<QuoteDocumentSnapshot>(d.SnapshotJson, QuotePricing.Json);
        if (snapshot is null) return QuoteOperationResult<QuoteAcceptanceDocumentBinding>.Failure(new("INTEGRATION_UNAVAILABLE", 503, "Issued document snapshot unavailable"));
        return QuoteOperationResult<QuoteAcceptanceDocumentBinding>.Success(new(d.DocumentId, d.QuoteId, d.RootQuoteId, d.QuoteRevision,
            d.SourceResourceVersion, d.ResultResourceVersion, d.CommercialFingerprint, d.PdfSha256, snapshot.Quote.BuyerRef));
    }
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static QuoteOperationResult<QuoteCustomerAcceptanceReadiness> Fail(QuoteOperationError e) => QuoteOperationResult<QuoteCustomerAcceptanceReadiness>.Failure(e);
}
