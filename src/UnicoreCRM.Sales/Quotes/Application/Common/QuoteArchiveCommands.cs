using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteArchiveCommands(QuoteAuthorization authorization, IQuotesPersistence persistence,
    IQuoteLifecyclePolicyReader policies, IQuoteWorkspacePolicyReader workspacePolicy,
    IEnumerable<IQuoteRetentionVerifier> verifiers, TimeProvider clock, QuoteLifecycleClock? lifecycleClock = null)
{
    private const string Operation = "archiveQuoteCommand";
    internal async Task<QuoteOperationResult<QuoteMutationResponse>> ExecuteAsync(string quoteId, QuoteArchiveRequest request,
        QuoteCommandMetadata command, CancellationToken ct)
    {
        var metadata = new QuoteRequestMetadata(command.RequestId, command.CorrelationId);
        var read = await authorization.AuthorizeAsync(metadata, ct);
        if (!read.IsSuccess) return Fail(read.Error!);
        var access = await authorization.AuthorizeAsync(metadata, ct, QuoteCapabilities.Delete);
        if (!access.IsSuccess) return Fail(access.Error!);
        if (!QuotePricing.EntityId(quoteId)) return Fail(QuoteErrors.NotFound());
        var fields = new Dictionary<string, string[]>();
        QuotePricing.Text(request.Reason, "reason", 500, true, fields);
        if (command.ExpectedVersion is null) fields["If-Match"] = ["Resource version required."];
        if (fields.Count != 0) return Fail(QuoteErrors.Validation(fields));
        var trusted = access.Value!.Trusted;
        var scope = QuoteApprovalAssessment.Hash($"{trusted.WorkspaceId}\n{trusted.MemberId}\n{Operation}\n{quoteId}\n{command.IdempotencyKey}");
        var fingerprint = QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new { quoteId, request, command.ExpectedVersion }, QuotePricing.Json));
        await using var transaction = await persistence.BeginAsync(ct);
        await persistence.LockAsync(scope, ct);
        await persistence.LockAsync($"aggregate:{trusted.WorkspaceId}:{quoteId}", ct);
        var quote = await persistence.LoadQuoteAsync(trusted.WorkspaceId, quoteId, ct);
        if (quote is null) return Fail(QuoteErrors.NotFound());
        quoteId = quote.QuoteId;
        foreach (var grant in new[] { read.Value!, access.Value })
        {
            var denied = await authorization.EnforceRecordAsync(grant, quote, Operation, metadata, ct);
            if (denied is not null) return Fail(denied);
            if (QuoteDeliveryCommands.ReadFields.Concat(new[] { "customerDecisionHistory", "archivedAt", "archiveReason" })
                .Any(f => !grant.Authorization.CanRead(f))) return Fail(QuoteErrors.AccessDenied());
        }
        if (!access.Value.Authorization.CanWrite("archivedAt") || !access.Value.Authorization.CanWrite("archiveReason")) return Fail(QuoteErrors.AccessDenied());
        var workspace = await workspacePolicy.ReadAsync(trusted.WorkspaceId, ct);
        if (workspace is null || !workspace.Enabled) return Fail(QuoteErrors.AccessDenied());
        var policy = policies.Read(trusted.WorkspaceId);
        if (policy is null) return Fail(new("INTEGRATION_UNAVAILABLE", 503, "Lifecycle policy unavailable", "QUOTE_LIFECYCLE_POLICY_MISSING_OR_INVALID"));
        if (!policy.AllowArchive) return Block("QUOTE_LIFECYCLE_POLICY_DISABLED");
        var replay = await persistence.FindCommandAsync(scope, ct);
        if (replay is not null)
        {
            if (replay.Fingerprint != fingerprint) return Fail(new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused"));
            var receipt = JsonSerializer.Deserialize<QuoteMutationResponse>(replay.ResponseJson, QuotePricing.Json)
                ?? throw new InvalidOperationException("Invalid archive receipt.");
            return Success(receipt with { Outcome = "REPLAYED", Result = new(QuoteProjection.Document(quote)) }, access.Value);
        }
        if (quote.ResourceVersion != command.ExpectedVersion) return Fail(new("VERSION_CONFLICT", 412, "Resource version conflict"));
        if (quote.ArchivedAt is not null || quote.Status is not ("DRAFT" or "REVIEW" or "REJECTED" or "EXPIRED")) return Block("QUOTE_STATE_INVALID");
        var participants = verifiers.ToArray();
        if (!QuoteRetentionVerification.Available(participants)) return Block("BLOCKED_CONTRACT");
        var leases = new List<IQuoteRetentionLease>();
        try
        {
            foreach (var participant in participants.OrderBy(p => p.Owner))
            {
                var lease = await participant.AcquireAsync(quoteId, metadata.RequestId, metadata.CorrelationId, ct);
                leases.Add(lease);
                if (lease.Eligibility == QuoteRetentionEligibility.Blocked) return Block("DOWNSTREAM_OBLIGATION");
                if (lease.Eligibility != QuoteRetentionEligibility.Clear) return Block("BLOCKED_CONTRACT");
            }
            var now = lifecycleClock?.GetUtcNow(trusted.WorkspaceId) ?? clock.GetUtcNow();
            var before = quote.ResourceVersion;
            var snapshot = QuoteProjection.Document(quote) with { CustomerDecisionHistory = null, DeliveryHistory = null };
            if (!quote.Archive(request.Reason, now)) return Block("QUOTE_STATE_INVALID");
            var id = "quote_command_" + Guid.NewGuid().ToString("N");
            var audit = "quote_audit_" + Guid.NewGuid().ToString("N");
            var eventId = "quote_event_" + Guid.NewGuid().ToString("N");
            var response = new QuoteMutationResponse(id, command.CorrelationId, quoteId, "QUOTE", quote.ResourceVersion,
                now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), "COMMITTED", new(QuoteProjection.Document(quote)), [], [eventId], [audit]);
            persistence.AddCommand(new(scope, trusted.WorkspaceId, trusted.MemberId, Operation, quoteId, fingerprint,
                JsonSerializer.Serialize(response, QuotePricing.Json), audit, command.RequestId, command.CorrelationId, before, quote.ResourceVersion, now));
            persistence.AddOutbox(new(eventId, trusted.WorkspaceId, quoteId, "QUOTE_ARCHIVED", command.CorrelationId,
                JsonSerializer.Serialize(new { workspaceId = trusted.WorkspaceId, quoteId, previousVersion = before,
                    resourceVersion = quote.ResourceVersion, actor = trusted.MemberId, archivedAt = response.OccurredAt,
                    request.Reason, policyVersion = policy.Version, verifiedOwners = participants.Select(p => p.Owner.ToString()).Order().ToArray(),
                    commercialFingerprint = QuoteApprovalAssessment.ContentFingerprint(quote), snapshot }, QuotePricing.Json), now));
            try { await persistence.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
            catch (QuotePersistenceConcurrencyException) { return Fail(new("VERSION_CONFLICT", 412, "Resource version conflict")); }
            return Success(response, access.Value);
        }
        finally { for (var i = leases.Count - 1; i >= 0; i--) await leases[i].DisposeAsync(); }
    }
    private static QuoteOperationResult<QuoteMutationResponse> Block(string detail) => Fail(new("QUOTE_ARCHIVE_BLOCKED", 409, "Quote archive blocked", detail));
    private static QuoteOperationResult<QuoteMutationResponse> Fail(QuoteOperationError e) => QuoteOperationResult<QuoteMutationResponse>.Failure(e);
    private static QuoteOperationResult<QuoteMutationResponse> Success(QuoteMutationResponse r, QuoteAccess a) =>
        QuoteOperationResult<QuoteMutationResponse>.Success(r with { Result = new(QuoteFieldSecurity.Project(r.Result.Quote, a.Authorization)) });
}
