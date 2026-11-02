using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteRevisionCommands(QuoteAuthorization authorization, IQuotesPersistence persistence,
    IQuoteLifecyclePolicyReader policies, IQuoteWorkspacePolicyReader workspacePolicy, IQuoteRelationshipVerifier relationships,
    IWorkspaceMemberReferenceValidator members, TimeProvider clock, QuoteLifecycleClock? lifecycleClock = null)
{
    private const string Operation = "reviseQuoteCommand";
    internal async Task<QuoteOperationResult<QuoteMutationResponse>> ExecuteAsync(string quoteId, QuoteRevisionRequest request,
        QuoteCommandMetadata command, CancellationToken ct)
    {
        var metadata = new QuoteRequestMetadata(command.RequestId, command.CorrelationId);
        var read = await authorization.AuthorizeAsync(metadata, ct);
        if (!read.IsSuccess) return Fail(read.Error!);
        var access = await authorization.AuthorizeAsync(metadata, ct, QuoteCapabilities.Create);
        if (!access.IsSuccess) return Fail(access.Error!);
        if (!QuotePricing.EntityId(quoteId)) return Fail(QuoteErrors.NotFound());
        var fields = new Dictionary<string, string[]>();
        QuotePricing.Text(request.Reason, "reason", 500, false, fields);
        if (command.ExpectedVersion is null) fields["If-Match"] = ["Resource version required."];
        if (fields.Count != 0) return Fail(QuoteErrors.Validation(fields));
        var trusted = access.Value!.Trusted;
        var policy = policies.Read(trusted.WorkspaceId);
        if (policy is null) return Fail(new("INTEGRATION_UNAVAILABLE", 503, "Lifecycle policy unavailable", "QUOTE_LIFECYCLE_POLICY_MISSING_OR_INVALID"));
        if (!policy.AllowRevision) return Block("QUOTE_LIFECYCLE_POLICY_DISABLED");
        var workspace = await workspacePolicy.ReadAsync(trusted.WorkspaceId, ct);
        if (workspace is null || !workspace.Enabled) return Fail(QuoteErrors.AccessDenied());
        var hash = QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new { quoteId, request, command.ExpectedVersion }, QuotePricing.Json));
        var scope = QuoteApprovalAssessment.Hash($"{trusted.WorkspaceId}\n{trusted.MemberId}\n{Operation}\n{quoteId}\n{command.IdempotencyKey}");
        await using var transaction = await persistence.BeginAsync(ct);
        await persistence.LockAsync(scope, ct);
        var preliminary = await persistence.ReadQuoteAsync(trusted.WorkspaceId, quoteId, ct);
        if (preliminary is null) return Fail(QuoteErrors.NotFound());
        if (!QuotePricing.EntityId(preliminary.RootQuoteId)) return Block("SOURCE_LINEAGE_INVALID");
        await persistence.LockAsync($"root-revision:{trusted.WorkspaceId}:{preliminary.RootQuoteId}", ct);
        await persistence.LockAsync($"aggregate:{trusted.WorkspaceId}:{quoteId}", ct);
        var source = await persistence.LoadQuoteAsync(trusted.WorkspaceId, quoteId, ct);
        if (source is null || source.RootQuoteId != preliminary.RootQuoteId) return Fail(QuoteErrors.NotFound());
        quoteId = source.QuoteId;
        // No tracked source mutation: root lock serializes all revision writers, including siblings.
        foreach (var grant in new[] { read.Value!, access.Value })
        {
            var denied = await authorization.EnforceRecordAsync(grant, source, Operation, metadata, ct);
            if (denied is not null) return Fail(denied);
            if (QuoteDeliveryCommands.ReadFields.Concat(new[] { "rootQuoteId", "quoteRevision", "ownerId", "customerDecisionHistory" })
                .Any(f => !grant.Authorization.CanRead(f))) return Fail(QuoteErrors.AccessDenied());
        }
        var replay = await persistence.FindCommandAsync(scope, ct);
        if (replay is not null)
        {
            if (replay.Fingerprint != hash) return Fail(new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused"));
            var receipt = JsonSerializer.Deserialize<QuoteMutationResponse>(replay.ResponseJson, QuotePricing.Json)
                ?? throw new InvalidOperationException("Invalid revision receipt.");
            var created = await persistence.ReadQuoteAsync(trusted.WorkspaceId, receipt.AggregateId, ct);
            if (created is null) return Fail(QuoteErrors.NotFound());
            foreach (var grant in new[] { read.Value!, access.Value })
            {
                var denied = await authorization.EnforceRecordAsync(grant, created, Operation, metadata, ct);
                if (denied is not null) return Fail(denied);
            }
            if (!CanCreate(QuoteProjection.Document(created), access.Value.Authorization)) return Fail(QuoteErrors.AccessDenied());
            return Success(receipt with { Outcome = "REPLAYED", Result = new(QuoteProjection.Document(created)) }, access.Value);
        }
        if (source.ResourceVersion != command.ExpectedVersion) return Fail(new("VERSION_CONFLICT", 412, "Resource version conflict"));
        if (source.ArchivedAt is not null || source.Status is not ("SENT" or "REJECTED" or "EXPIRED")) return Block("QUOTE_STATE_INVALID");
        var root = await persistence.ReadQuoteAsync(trusted.WorkspaceId, source.RootQuoteId, ct);
        if (root is null || root.RootQuoteId != root.QuoteId || root.QuoteRevision != 1) return Block("SOURCE_LINEAGE_INVALID");
        var state = await persistence.ReadRootRevisionStateAsync(trusted.WorkspaceId, source.RootQuoteId, ct);
        if (state.HasWorkingRevision) return Block("ROOT_HAS_WORKING_REVISION");
        if (state.MaximumRevision == int.MaxValue) return Block("REVISION_LIMIT_REACHED");
        if (!workspace.EnabledCurrencies.Contains(source.Currency, StringComparer.Ordinal)) return Block("CURRENCY_DISABLED");
        if (!QuotePricing.EntityId(source.OwnerId) || !await members.IsActiveMemberAsync(trusted.WorkspaceId, source.OwnerId!, ct)) return Block("SOURCE_OWNER_UNAVAILABLE");
        if (!await relationships.VerifyBuyerAsync(new(source.BuyerType, source.BuyerId), metadata.RequestId, metadata.CorrelationId, ct)) return Block("SOURCE_BUYER_UNAVAILABLE");
        IQuoteDealSourceLease? lease = null;
        try
        {
            if (source.SourcePath == "DEAL")
            {
                if (source.SourceDealId is null) return Block("SOURCE_LINEAGE_INVALID");
                lease = await relationships.AcquireDealAsync(source.SourceDealId, null, true, metadata.RequestId, metadata.CorrelationId, ct);
                var deal = lease.Source;
                if (lease.ErrorCode is not null || deal is null) return Block("SOURCE_DEAL_UNAVAILABLE");
                if (deal.BuyerRef != new QuoteBuyerReference(source.BuyerType, source.BuyerId) || deal.ContactId != source.ContactId
                    || deal.SourceLeadId != source.SourceLeadId) return Block("SOURCE_RELATIONSHIP_CHANGED");
            }
            var id = "quote_" + Guid.NewGuid().ToString("N");
            var content = QuoteRevisionContent.Build(source, id);
            if (!content.IsSuccess) return Fail(content.Error!);
            var number = await persistence.NextNumberAsync(ct);
            var now = lifecycleClock?.GetUtcNow(trusted.WorkspaceId) ?? clock.GetUtcNow();
            var created = source.CreateRevision(id, number, state.MaximumRevision + 1, content.Value!, now);
            if (created is null) return Block("QUOTE_STATE_INVALID");
            var denied = await authorization.EnforceRecordAsync(access.Value, created, Operation, metadata, ct);
            if (denied is not null || !CanCreate(QuoteProjection.Document(created), access.Value.Authorization)) return Fail(QuoteErrors.AccessDenied());
            var commandId = "quote_command_" + Guid.NewGuid().ToString("N");
            var auditId = "quote_audit_" + Guid.NewGuid().ToString("N");
            var eventId = "quote_event_" + Guid.NewGuid().ToString("N");
            var response = new QuoteMutationResponse(commandId, command.CorrelationId, id, "QUOTE", created.ResourceVersion,
                now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), "COMMITTED", new(QuoteProjection.Document(created)), [], [eventId], [auditId]);
            persistence.AddQuote(created);
            persistence.AddCommand(new(scope, trusted.WorkspaceId, trusted.MemberId, Operation, id, hash,
                JsonSerializer.Serialize(response, QuotePricing.Json), auditId, command.RequestId, command.CorrelationId, null, created.ResourceVersion, now));
            persistence.AddOutbox(new(eventId, trusted.WorkspaceId, id, "QUOTE_REVISION_CREATED", command.CorrelationId,
                JsonSerializer.Serialize(new { workspaceId = trusted.WorkspaceId, sourceQuoteId = quoteId,
                    sourceResourceVersion = source.ResourceVersion, rootQuoteId = source.RootQuoteId, quoteId = id,
                    quoteRevision = created.QuoteRevision, resourceVersion = created.ResourceVersion, policyVersion = policy.Version,
                    actor = trusted.MemberId, request.Reason, sourceCommercialFingerprint = QuoteApprovalAssessment.ContentFingerprint(source),
                    commercialFingerprint = QuoteApprovalAssessment.ContentFingerprint(created), snapshot = response.Result.Quote }, QuotePricing.Json), now));
            try { await persistence.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
            catch (QuotePersistenceConcurrencyException) { return Fail(new("VERSION_CONFLICT", 412, "Resource version conflict")); }
            return Success(response, access.Value);
        }
        finally { if (lease is not null) await lease.DisposeAsync(); }
    }
    internal static bool CanCreate(QuoteReadModel model, RecordAccessAuthorization access) =>
        access.CanWrite("status") && access.CanWrite("quoteRevision") && access.CanWrite("rootQuoteId")
        && access.CanWrite("revisionOfQuoteId")
        && QuoteDraftCommands.Values(model).All(p => p.Value == "null" || access.CanWrite(p.Key))
        && (model.SenderName is null || access.CanWrite("senderName"))
        && (model.SenderAddress is null || access.CanWrite("senderAddress"))
        && (model.SenderEmail is null || access.CanWrite("senderEmail"))
        && (model.SenderTaxId is null || access.CanWrite("senderTaxId"));
    private static QuoteOperationResult<QuoteMutationResponse> Block(string detail) => Fail(new("QUOTE_REVISION_BLOCKED", 409, "Quote revision blocked", detail));
    private static QuoteOperationResult<QuoteMutationResponse> Fail(QuoteOperationError e) => QuoteOperationResult<QuoteMutationResponse>.Failure(e);
    private static QuoteOperationResult<QuoteMutationResponse> Success(QuoteMutationResponse r, QuoteAccess a) =>
        QuoteOperationResult<QuoteMutationResponse>.Success(r with { Result = new(QuoteFieldSecurity.Project(r.Result.Quote, a.Authorization)) });
}
