using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed record QuoteDocumentSnapshot(QuoteReadModel Quote, QuoteSellerSnapshot Seller, QuoteDocumentPartySnapshot Buyer,
    string IssuedAt, string DocumentId, string TemplateVersion, string? WorkspaceTimeZone = null);

internal sealed class QuoteDocumentCommands(QuoteAuthorization authorization, IQuotesPersistence quotes, IQuoteDocumentPersistence documents,
    IQuoteDocumentPolicyReader policy, IQuoteWorkspacePolicyReader workspacePolicy, IQuoteApprovalPolicyReader approvalPolicy,
    IWorkspaceTimeZoneReader zones, IQuoteSellerReader sellers, IEnumerable<IQuoteDocumentPartyReader> parties,
    IQuotePdfRenderer renderer, TimeProvider clock, IQuoteDocumentCommitProbe? commitProbe = null, QuoteSalesPolicyReader? salesPolicies = null)
{
    internal const string Operation = "issueQuoteDocumentCommand";
    internal const string Template = "quote-a4-v2-noto-b85c38ecea8a";
    internal static readonly string[] RenderFields = ["buyerRef", "sourceDealId", "title", "currency", "lineItems", "adjustments",
        "subtotal", "discountTotal", "taxTotal", "grandTotal", "validUntil", "notes", "paymentAgreement"];

    internal async Task<QuoteOperationResult<QuoteDocumentIssueResponse>> IssueAsync(string quoteId, IssueQuoteDocumentRequest request,
        QuoteCommandMetadata command, CancellationToken ct)
    {
        // Retained constructor compatibility for historical consumers; B1 is not an issuance prerequisite.
        _ = approvalPolicy;
        _ = salesPolicies;
        var metadata = new QuoteRequestMetadata(command.RequestId, command.CorrelationId);
        var read = await authorization.AuthorizeAsync(metadata, ct);
        if (!read.IsSuccess) return Fail(read.Error!);
        var issue = await authorization.AuthorizeAsync(metadata, ct, QuoteCapabilities.IssueDocument);
        if (!issue.IsSuccess) return Fail(issue.Error!);
        if (!QuotePricing.EntityId(quoteId)) return Fail(QuoteErrors.NotFound());
        var fields = new Dictionary<string, string[]>(); QuotePricing.Text(request.Reason, "reason", 500, false, fields);
        if (command.ExpectedVersion is null) fields["If-Match"] = ["Resource version required."];
        if (fields.Count != 0) return Fail(QuoteErrors.Validation(fields));
        var trusted = issue.Value!.Trusted;
        var scope = QuoteApprovalAssessment.Hash($"{trusted.WorkspaceId}\n{trusted.MemberId}\n{Operation}\n{quoteId.ToLowerInvariant()}\n{command.IdempotencyKey}");
        var fingerprint = QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new { quoteId = quoteId.ToLowerInvariant(), request, command.ExpectedVersion }, QuotePricing.Json));
        await using var transaction = await quotes.BeginAsync(ct);
        await quotes.LockAsync(scope, ct); await quotes.LockAsync($"aggregate:{trusted.WorkspaceId}:{quoteId}", ct);
        var quote = await quotes.LoadQuoteAsync(trusted.WorkspaceId, quoteId, ct);
        if (quote is null) return Fail(QuoteErrors.NotFound());
        foreach (var grant in new[] { read.Value!, issue.Value })
        {
            var error = await authorization.EnforceRecordAsync(grant, quote, Operation, metadata, ct);
            if (error is not null) return Fail(error);
        }
        var replay = await quotes.FindCommandAsync(scope, ct);
        if (replay is not null)
        {
            if (replay.Fingerprint != fingerprint) return Fail(new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused"));
            var receipt = JsonSerializer.Deserialize<QuoteDocumentIssueResponse>(replay.ResponseJson, QuotePricing.Json)
                ?? throw new InvalidOperationException("Invalid document receipt");
            var stored = await documents.FindAsync(trusted.WorkspaceId, quote.QuoteId, receipt.Result.Document.DocumentId, ct);
            if (stored is null || !await CanReadStoredAsync(stored, [read.Value!, issue.Value], metadata, ct)) return Fail(QuoteErrors.AccessDenied());
            return QuoteOperationResult<QuoteDocumentIssueResponse>.Success(receipt with { Outcome = "REPLAYED",
                Result = new(QuoteFieldSecurity.Project(QuoteProjection.Document(quote), read.Value!.Authorization), receipt.Result.Document) });
        }
        if (quote.ResourceVersion != command.ExpectedVersion) return Fail(new("VERSION_CONFLICT", 412, "Resource version conflict"));
        if (RenderFields.Any(f => !read.Value!.Authorization.CanRead(f) || !issue.Value.Authorization.CanRead(f))) return Fail(QuoteErrors.AccessDenied());
        if (quote.ArchivedAt is not null || quote.Status is not ("DRAFT" or "REVIEW" or "SENT")) return Block("QUOTE_STATE_INVALID");
        var workspace = await workspacePolicy.ReadAsync(trusted.WorkspaceId, ct);
        if (workspace is null || !workspace.Enabled) return Fail(QuoteErrors.AccessDenied());
        var version = policy.EnabledVersion(trusted.WorkspaceId); if (version is null) return Block("DOCUMENT_POLICY_MISSING_OR_DISABLED");
        if (quote.SubtotalCurrency != quote.Currency || quote.DiscountTotalCurrency != quote.Currency || quote.TaxTotalCurrency != quote.Currency
            || quote.GrandTotalCurrency != quote.Currency || !QuoteRevisionContent.Build(quote, quote.QuoteId).IsSuccess) return Block("COMMERCIAL_SNAPSHOT_INVALID");
        var now = clock.GetUtcNow(); var zone = await zones.ReadTimeZoneAsync(trusted.WorkspaceId, ct);
        var validity = QuoteValidity.Evaluate(quote.ValidUntil, zone, now);
        if (validity.State is "OVERDUE" or "TIMEZONE_UNAVAILABLE") return Block(validity.State == "OVERDUE" ? "QUOTE_EXPIRED" : "WORKSPACE_TIMEZONE_UNAVAILABLE");
        var commercialFingerprint = QuoteApprovalAssessment.ContentFingerprint(quote);
        // Global lease order: Quote aggregate -> Workspace seller -> exactly one buyer owner. Hold through commit.
        await using var seller = await sellers.AcquireAsync(metadata.RequestId, metadata.CorrelationId, ct);
        if (seller.ErrorCode is not null) return Fail(QuoteErrors.AccessDenied());
        if (seller.Snapshot is not { } issuer || issuer.WorkspaceId != trusted.WorkspaceId) return Block(seller.Blockers.FirstOrDefault() ?? "SELLER_UNAVAILABLE");
        var currentValidity = QuoteValidity.Evaluate(quote.ValidUntil, issuer.TimeZone, clock.GetUtcNow());
        if (currentValidity.State is "OVERDUE" or "TIMEZONE_UNAVAILABLE") return Block(currentValidity.State == "OVERDUE" ? "QUOTE_EXPIRED" : "WORKSPACE_TIMEZONE_UNAVAILABLE");
        var currentWorkspace = await workspacePolicy.ReadAsync(trusted.WorkspaceId, ct);
        if (currentWorkspace is null || !currentWorkspace.Enabled || !currentWorkspace.EnabledCurrencies.Contains(quote.Currency, StringComparer.Ordinal)) return Block("QUOTE_CURRENCY_NOT_ENABLED");
        var readers = parties.Where(x => x.BuyerType == quote.BuyerType).ToArray(); if (readers.Length != 1) return Block("BLOCKED_CONTRACT_BUYER");
        await using var buyer = await readers[0].AcquireAsync(quote.BuyerId, metadata.RequestId, metadata.CorrelationId, ct);
        if (buyer.Snapshot is not { Eligible: true } party || string.IsNullOrWhiteSpace(party.DisplayName)) return Fail(QuoteErrors.AccessDenied());
        var sellerHash = QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(issuer with { ConfigurationVersion = 0 }, QuotePricing.Json));
        var partyHash = QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(party with { Version = 0 }, QuotePricing.Json));
        var binding = QuoteApprovalAssessment.Hash(string.Join("|", commercialFingerprint, sellerHash, partyHash, Template, version, QuoteDirectIssuancePolicy.Version));
        if (await documents.FindBindingAsync(trusted.WorkspaceId, quote.QuoteId, binding, ct) is not null) return Block("DOCUMENT_ALREADY_ISSUED");
        var id = "qdoc_" + Guid.NewGuid().ToString("N"); var before = quote.ResourceVersion;
        var snapshot = JsonSerializer.Serialize(new QuoteDocumentSnapshot(QuoteProjection.Document(quote) with { DeliveryHistory = null, CustomerDecisionHistory = null },
            issuer, party, now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), id, Template, issuer.TimeZone), QuotePricing.Json);
        QuotePdfRenderResult rendered;
        try { rendered = await renderer.RenderAsync(snapshot, ct); }
        catch (QuotePdfException error) { return Block(error.Code); }
        if (QuoteValidity.Evaluate(quote.ValidUntil, issuer.TimeZone, clock.GetUtcNow()).State == "OVERDUE") return Block("QUOTE_EXPIRED");
        var document = new QuoteDocument(id, trusted.WorkspaceId, quote.QuoteId, quote.RootQuoteId, quote.QuoteRevision, before,
            binding, commercialFingerprint, snapshot, issuer.ConfigurationVersion, QuoteDirectIssuancePolicy.Version, version, Template,
            await documents.NextVersionAsync(trusted.WorkspaceId, quote.QuoteId, ct), trusted.MemberId, now,
            Convert.ToHexString(SHA256.HashData(rendered.Bytes)), rendered.Bytes.Length, rendered.PageCount, JsonSerializer.Serialize(RenderFields));
        if (!quote.RecordDocumentIssued(now)) return Block("QUOTE_STATE_INVALID");
        var commandId = "quote_command_" + Guid.NewGuid().ToString("N"); var audit = "quote_audit_" + Guid.NewGuid().ToString("N"); var eventId = "quote_event_" + Guid.NewGuid().ToString("N");
        var result = new QuoteDocumentIssueResponse(commandId, command.CorrelationId, quote.QuoteId, "QUOTE", quote.ResourceVersion,
            now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), "COMMITTED", new(QuoteProjection.Document(quote), Metadata(document, quote.QuoteNumber)), [eventId], [audit]);
        documents.Add(document, rendered.Bytes);
        quotes.AddCommand(new(scope, trusted.WorkspaceId, trusted.MemberId, Operation, quote.QuoteId, fingerprint, JsonSerializer.Serialize(result, QuotePricing.Json), audit,
            command.RequestId, command.CorrelationId, before, quote.ResourceVersion, now));
        quotes.AddOutbox(new(eventId, trusted.WorkspaceId, quote.QuoteId, "QUOTE_DOCUMENT_ISSUED", command.CorrelationId,
            JsonSerializer.Serialize(new { documentId = id, quoteRevision = quote.QuoteRevision, contentResourceVersion = before, resourceVersion = quote.ResourceVersion,
                document.PdfSha256, document.FileSize, document.PageCount, bindingHash = binding, commercialFingerprint = commercialFingerprint,
                sellerVersion = issuer.ConfigurationVersion, policyVersion = version, actor = trusted.MemberId, renderMs = rendered.DurationMs, peakMemoryBytes = rendered.PeakMemoryBytes }, QuotePricing.Json), now));
        try { await quotes.SaveChangesAsync(ct); commitProbe?.AfterStorageFlush(trusted.WorkspaceId); await transaction.CommitAsync(ct); }
        catch (QuotePersistenceConcurrencyException) { return Fail(new("VERSION_CONFLICT", 412, "Resource version conflict")); }
        return QuoteOperationResult<QuoteDocumentIssueResponse>.Success(result with { Result = result.Result with { Quote = QuoteFieldSecurity.Project(result.Result.Quote, read.Value!.Authorization) } });
    }

    internal async Task<QuoteActionAvailabilityReadModel> AvailabilityAsync(Quote quote, QuoteAccess read, QuoteRequestMetadata metadata, CancellationToken ct)
    {
        static QuoteActionAvailabilityReadModel Blocked(string code) => new(false, [code]);
        var issue = await authorization.AuthorizeAsync(metadata, ct, QuoteCapabilities.IssueDocument);
        if (!issue.IsSuccess || RenderFields.Any(f => !read.Authorization.CanRead(f) || !issue.Value!.Authorization.CanRead(f))) return Blocked("ACCESS_DENIED");
        if (await authorization.EnforceRecordAsync(issue.Value!, quote, Operation, metadata, ct) is not null) return Blocked("ACCESS_DENIED");
        if (quote.ArchivedAt is not null || quote.Status is not ("DRAFT" or "REVIEW" or "SENT")) return Blocked("QUOTE_STATE_INVALID");
        if (policy.EnabledVersion(quote.WorkspaceId) is null) return Blocked("DOCUMENT_POLICY_MISSING_OR_DISABLED");
        var validity = QuoteValidity.Evaluate(quote.ValidUntil, await zones.ReadTimeZoneAsync(quote.WorkspaceId, ct), clock.GetUtcNow());
        if (validity.State is "OVERDUE" or "TIMEZONE_UNAVAILABLE") return Blocked(validity.State == "OVERDUE" ? "QUOTE_EXPIRED" : "WORKSPACE_TIMEZONE_UNAVAILABLE");
        await using var seller = await sellers.AcquireAsync(metadata.RequestId, metadata.CorrelationId, ct);
        if (seller.ErrorCode is not null || seller.Snapshot is null) return Blocked(seller.Blockers.FirstOrDefault() ?? "SELLER_UNAVAILABLE");
        var capturedValidity = QuoteValidity.Evaluate(quote.ValidUntil, seller.Snapshot.TimeZone, clock.GetUtcNow());
        if (capturedValidity.State is "OVERDUE" or "TIMEZONE_UNAVAILABLE") return Blocked(capturedValidity.State == "OVERDUE" ? "QUOTE_EXPIRED" : "WORKSPACE_TIMEZONE_UNAVAILABLE");
        var currentWorkspace = await workspacePolicy.ReadAsync(quote.WorkspaceId, ct);
        if (currentWorkspace is null || !currentWorkspace.Enabled || !currentWorkspace.EnabledCurrencies.Contains(quote.Currency, StringComparer.Ordinal)) return Blocked("QUOTE_CURRENCY_NOT_ENABLED");
        var readers = parties.Where(x => x.BuyerType == quote.BuyerType).ToArray(); if (readers.Length != 1) return Blocked("BLOCKED_CONTRACT_BUYER");
        await using var buyer = await readers[0].AcquireAsync(quote.BuyerId, metadata.RequestId, metadata.CorrelationId, ct);
        if (buyer.Snapshot is not { Eligible: true } captured) return Blocked("ACCESS_DENIED");
        var sellerHash = QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(seller.Snapshot with { ConfigurationVersion = 0 }, QuotePricing.Json));
        var buyerHash = QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(captured with { Version = 0 }, QuotePricing.Json));
        var binding = QuoteApprovalAssessment.Hash(string.Join("|", QuoteApprovalAssessment.ContentFingerprint(quote), sellerHash, buyerHash, Template, policy.EnabledVersion(quote.WorkspaceId), QuoteDirectIssuancePolicy.Version));
        if (await documents.FindBindingAsync(quote.WorkspaceId, quote.QuoteId, binding, ct) is not null) return Blocked("DOCUMENT_ALREADY_ISSUED");
        return new(true, []);
    }

    internal async Task<bool> CanReadStoredAsync(QuoteDocument document, IReadOnlyList<QuoteAccess> grants, QuoteRequestMetadata metadata, CancellationToken ct)
    {
        var manifest = JsonSerializer.Deserialize<string[]>(document.FieldManifestJson);
        if (manifest is null || manifest.Length == 0 || manifest.Any(f => !RenderFields.Contains(f, StringComparer.Ordinal) || grants.Any(g => !g.Authorization.CanRead(f)))) return false;
        var snapshot = JsonSerializer.Deserialize<QuoteDocumentSnapshot>(document.SnapshotJson, QuotePricing.Json);
        if (snapshot is null) return false;
        var readers = parties.Where(x => x.BuyerType == snapshot.Buyer.Type).ToArray(); if (readers.Length != 1) return false;
        await using var lease = await readers[0].AcquireAsync(snapshot.Buyer.Id, metadata.RequestId, metadata.CorrelationId, ct);
        return lease.Snapshot is not null;
    }
    internal static QuoteIssuedDocument Metadata(QuoteDocument d, string quoteNumber) => new(d.DocumentId, d.QuoteId, d.RootQuoteId, d.QuoteRevision, d.DocumentVersion,
        d.SourceResourceVersion, d.ResultResourceVersion, d.CommercialFingerprint, d.SellerConfigurationVersion, d.ApprovalPolicyVersion, d.PolicyVersion, d.TemplateVersion,
        d.IssuedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), d.IssuedBy, "ISSUED", d.FileSize, d.PageCount, "application/pdf", d.PdfSha256,
        quoteNumber + "-r" + d.QuoteRevision + "-d" + d.DocumentVersion + ".pdf");
    private static QuoteOperationResult<QuoteDocumentIssueResponse> Block(string detail) => Fail(new("QUOTE_DOCUMENT_BLOCKED", 409, "Quote document issuance blocked", detail));
    private static QuoteOperationResult<QuoteDocumentIssueResponse> Fail(QuoteOperationError e) => QuoteOperationResult<QuoteDocumentIssueResponse>.Failure(e);
}
