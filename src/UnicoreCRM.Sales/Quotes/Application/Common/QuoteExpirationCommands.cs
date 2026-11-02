using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteExpirationCommands(QuoteAuthorization authorization, IQuotesPersistence persistence,
    IQuoteLifecyclePolicyReader policies, IQuoteWorkspacePolicyReader workspacePolicy, IWorkspaceTimeZoneReader timeZones, TimeProvider clock, QuoteLifecycleClock? lifecycleClock = null)
{
    private const string Operation = "expireQuoteCommand";
    internal async Task<QuoteOperationResult<QuoteMutationResponse>> ExecuteAsync(string quoteId, QuoteExpirationRequest request,
        QuoteCommandMetadata command, CancellationToken ct)
    {
        var metadata = new QuoteRequestMetadata(command.RequestId, command.CorrelationId);
        var read = await authorization.AuthorizeAsync(metadata, ct);
        if (!read.IsSuccess) return Fail(read.Error!);
        var access = await authorization.AuthorizeAsync(metadata, ct, QuoteCapabilities.Update);
        if (!access.IsSuccess) return Fail(access.Error!);
        if (!QuotePricing.EntityId(quoteId)) return Fail(QuoteErrors.NotFound());
        var fields = new Dictionary<string, string[]>();
        QuotePricing.Text(request.Reason, "reason", 500, false, fields);
        if (command.ExpectedVersion is null) fields["If-Match"] = ["Resource version required."];
        if (fields.Count != 0) return Fail(QuoteErrors.Validation(fields));
        var trusted = access.Value!.Trusted;
        var scope = QuoteApprovalAssessment.Hash($"{trusted.WorkspaceId}\n{trusted.MemberId}\n{Operation}\n{quoteId}\n{command.IdempotencyKey}");
        var hash = QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new { quoteId, request, command.ExpectedVersion }, QuotePricing.Json));
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
            if (QuoteDeliveryCommands.ReadFields.Concat(new[] { "customerDecisionHistory", "expiredAt" }).Any(f => !grant.Authorization.CanRead(f))) return Fail(QuoteErrors.AccessDenied());
        }
        if (!access.Value.Authorization.CanWrite("status") || !access.Value.Authorization.CanWrite("expiredAt")) return Fail(QuoteErrors.AccessDenied());
        var workspace = await workspacePolicy.ReadAsync(trusted.WorkspaceId, ct);
        if (workspace is null || !workspace.Enabled) return Fail(QuoteErrors.AccessDenied());
        var policy = policies.Read(trusted.WorkspaceId);
        if (policy is null) return Fail(new("INTEGRATION_UNAVAILABLE", 503, "Lifecycle policy unavailable", "QUOTE_LIFECYCLE_POLICY_MISSING_OR_INVALID"));
        if (!policy.AllowExpiration) return Block("QUOTE_LIFECYCLE_POLICY_DISABLED");
        var replay = await persistence.FindCommandAsync(scope, ct);
        if (replay is not null)
        {
            if (replay.Fingerprint != hash) return Fail(new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused"));
            var receipt = JsonSerializer.Deserialize<QuoteMutationResponse>(replay.ResponseJson, QuotePricing.Json)
                ?? throw new InvalidOperationException("Invalid expiration receipt.");
            return Success(receipt with { Outcome = "REPLAYED", Result = new(QuoteProjection.Document(quote)) }, access.Value);
        }
        if (quote.ResourceVersion != command.ExpectedVersion) return Fail(new("VERSION_CONFLICT", 412, "Resource version conflict"));
        var now = lifecycleClock?.GetUtcNow(trusted.WorkspaceId) ?? clock.GetUtcNow();
        var zone = await timeZones.ReadTimeZoneAsync(trusted.WorkspaceId, ct);
        var validity = QuoteValidity.Evaluate(quote.ValidUntil, zone, now);
        if (validity.State == "TIMEZONE_UNAVAILABLE") return Block("WORKSPACE_TIMEZONE_INVALID");
        if (validity.State != "OVERDUE") return Block("QUOTE_NOT_OVERDUE");
        var before = quote.ResourceVersion;
        var snapshot = QuoteProjection.Document(quote) with { CustomerDecisionHistory = null, DeliveryHistory = null };
        var date = DateOnly.ParseExact(validity.LocalDate!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!quote.Expire(date, now)) return Block("QUOTE_STATE_INVALID");
        var id = "quote_command_" + Guid.NewGuid().ToString("N");
        var audit = "quote_audit_" + Guid.NewGuid().ToString("N");
        var eventId = "quote_event_" + Guid.NewGuid().ToString("N");
        var response = new QuoteMutationResponse(id, command.CorrelationId, quoteId, "QUOTE", quote.ResourceVersion,
            now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), "COMMITTED", new(QuoteProjection.Document(quote)), [], [eventId], [audit]);
        persistence.AddCommand(new(scope, trusted.WorkspaceId, trusted.MemberId, Operation, quoteId, hash,
            JsonSerializer.Serialize(response, QuotePricing.Json), audit, command.RequestId, command.CorrelationId, before, quote.ResourceVersion, now));
        persistence.AddOutbox(new(eventId, trusted.WorkspaceId, quoteId, "QUOTE_EXPIRED", command.CorrelationId,
            JsonSerializer.Serialize(new { workspaceId = trusted.WorkspaceId, quoteId, previousVersion = before,
                resourceVersion = quote.ResourceVersion, actor = trusted.MemberId, request.Reason, expiredAt = response.OccurredAt,
                policyVersion = policy.Version, validity, snapshot }, QuotePricing.Json), now));
        try { await persistence.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (QuotePersistenceConcurrencyException) { return Fail(new("VERSION_CONFLICT", 412, "Resource version conflict")); }
        return Success(response, access.Value);
    }
    private static QuoteOperationResult<QuoteMutationResponse> Block(string detail) => Fail(new("QUOTE_EXPIRATION_BLOCKED", 409, "Quote expiration blocked", detail));
    private static QuoteOperationResult<QuoteMutationResponse> Fail(QuoteOperationError e) => QuoteOperationResult<QuoteMutationResponse>.Failure(e);
    private static QuoteOperationResult<QuoteMutationResponse> Success(QuoteMutationResponse r, QuoteAccess a) =>
        QuoteOperationResult<QuoteMutationResponse>.Success(r with { Result = new(QuoteFieldSecurity.Project(r.Result.Quote, a.Authorization)) });
}
