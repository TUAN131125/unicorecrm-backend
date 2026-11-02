using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteCustomerResponseCommands(QuoteAuthorization authorization, IQuotesPersistence persistence,
    IQuoteCustomerDecisionPolicyReader policies, IQuoteWorkspacePolicyReader workspacePolicy, TimeProvider clock, QuoteLifecycleClock? lifecycleClock = null)
{
    private const string Operation = "recordQuoteCustomerDecisionCommand";

    internal async Task<QuoteOperationResult<QuoteMutationResponse>> ExecuteAsync(string quoteId, RecordQuoteCustomerDecisionRequest request,
        QuoteCommandMetadata command, CancellationToken ct)
    {
        var metadata = new QuoteRequestMetadata(command.RequestId, command.CorrelationId);
        var read = await authorization.AuthorizeAsync(metadata, ct);
        if (!read.IsSuccess) return Fail(read.Error!);
        var access = await authorization.AuthorizeAsync(metadata, ct, QuoteCapabilities.Update);
        if (!access.IsSuccess) return Fail(access.Error!);
        if (!QuotePricing.EntityId(quoteId)) return Fail(QuoteErrors.NotFound());
        var trusted = access.Value!.Trusted;
        var validation = Validate(request, command, lifecycleClock?.GetUtcNow(trusted.WorkspaceId) ?? clock.GetUtcNow());
        if (validation is not null) return Fail(validation);
        var payloadHash = QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new { quoteId, request, actor = trusted.MemberId }, QuotePricing.Json));
        var fingerprint = QuoteApprovalAssessment.Hash(payloadHash + "\n" + command.ExpectedVersion);
        var scope = QuoteApprovalAssessment.Hash($"{trusted.WorkspaceId}\n{trusted.MemberId}\n{Operation}\n{quoteId}\n{command.IdempotencyKey}");
        await using var transaction = await persistence.BeginAsync(ct);
        await persistence.LockAsync(scope, ct);
        await persistence.LockAsync("customer-decision:" + QuoteApprovalAssessment.Hash(trusted.WorkspaceId + "\n" + request.DecisionId), ct);
        await persistence.LockAsync($"aggregate:{trusted.WorkspaceId}:{quoteId}", ct);
        var quote = await persistence.LoadQuoteAsync(trusted.WorkspaceId, quoteId, ct);
        if (quote is null) return Fail(QuoteErrors.NotFound());
        foreach (var grant in new[] { read.Value!, access.Value })
        {
            var denied = await authorization.EnforceRecordAsync(grant, quote, Operation, metadata, ct);
            if (denied is not null) return Fail(denied);
        }
        if (!access.Value.Authorization.CanWrite("customerDecisionHistory")
            || !access.Value.Authorization.CanRead("customerDecisionHistory") || !read.Value!.Authorization.CanRead("customerDecisionHistory")
            || QuoteDeliveryCommands.ReadFields.Any(field => !access.Value.Authorization.CanRead(field) || !read.Value.Authorization.CanRead(field)))
            return Fail(QuoteErrors.AccessDenied());
        var workspace = await workspacePolicy.ReadAsync(trusted.WorkspaceId, ct);
        if (workspace is null || !workspace.Enabled) return Fail(QuoteErrors.AccessDenied());
        var policy = policies.Read(trusted.WorkspaceId);
        if (policy is null) return Error("INTEGRATION_UNAVAILABLE", 503, "Customer decision policy unavailable", "CUSTOMER_DECISION_POLICY_MISSING_OR_INVALID");
        if (!policy.AllowReportedAcceptance) return Error("QUOTE_CUSTOMER_RESPONSE_BLOCKED", 409, "Customer response recording disabled", "CUSTOMER_DECISION_POLICY_DISABLED");
        var replay = await persistence.FindCommandAsync(scope, ct);
        if (replay is not null)
        {
            if (replay.Fingerprint != fingerprint) return Error("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused");
            var receipt = JsonSerializer.Deserialize<QuoteMutationResponse>(replay.ResponseJson, QuotePricing.Json)
                ?? throw new InvalidOperationException("Invalid customer response receipt.");
            return Success(receipt with { Outcome = "REPLAYED", Result = new(QuoteProjection.Document(quote, policy)) }, access.Value);
        }
        var existing = await persistence.FindCustomerDecisionAsync(trusted.WorkspaceId, request.DecisionId, ct);
        if (existing is not null)
        {
            if (existing.QuoteId != quoteId || existing.RequestFingerprint != payloadHash)
                return Error("QUOTE_CUSTOMER_DECISION_ID_REUSED", 409, "Decision identity already used");
            var receipt = new QuoteMutationResponse(existing.CommandId, command.CorrelationId, quoteId, "QUOTE",
                existing.ResultResourceVersion, existing.RecordedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), "REPLAYED",
                new(QuoteProjection.Document(quote, policy)), [], [existing.EventId], [existing.AuditId]);
            persistence.AddCommand(new(scope, trusted.WorkspaceId, trusted.MemberId, Operation, quoteId, fingerprint,
                JsonSerializer.Serialize(receipt, QuotePricing.Json), "quote_audit_" + Guid.NewGuid().ToString("N"), command.RequestId,
                command.CorrelationId, quote.ResourceVersion, quote.ResourceVersion, (lifecycleClock?.GetUtcNow(trusted.WorkspaceId) ?? clock.GetUtcNow())));
            await persistence.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Success(receipt, access.Value);
        }
        if (quote.ResourceVersion != command.ExpectedVersion) return Error("VERSION_CONFLICT", 412, "Resource version conflict");
        var now = (lifecycleClock?.GetUtcNow(trusted.WorkspaceId) ?? clock.GetUtcNow());
        validation = Validate(request, command, now);
        if (validation is not null) return Fail(validation);
        var snapshot = QuoteProjection.Document(quote) with { CustomerDecisionHistory = null, DeliveryHistory = null };
        var previousVersion = quote.ResourceVersion;
        var eventId = "quote_event_" + Guid.NewGuid().ToString("N");
        var auditId = "quote_audit_" + Guid.NewGuid().ToString("N");
        var commandId = "quote_command_" + Guid.NewGuid().ToString("N");
        var record = QuoteCustomerDecisionRecords.Create(quote, request.DecisionId, trusted.MemberId, "ACCEPTANCE", request.SourceChannel,
            request.SourceDescription, request.ReportedCustomerDecisionAt, null, request.Note, policy, payloadHash, commandId, auditId, eventId, now);
        if (!quote.RecordReportedAcceptance(record, now)) return Error("QUOTE_CUSTOMER_RESPONSE_BLOCKED", 409, "Customer response cannot be recorded", "QUOTE_STATE_INVALID");
        var result = new QuoteMutationResponse(commandId, command.CorrelationId, quoteId, "QUOTE", quote.ResourceVersion,
            now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), "COMMITTED", new(QuoteProjection.Document(quote, policy)), [], [eventId], [auditId]);
        persistence.AddCommand(new(scope, trusted.WorkspaceId, trusted.MemberId, Operation, quoteId, fingerprint,
            JsonSerializer.Serialize(result, QuotePricing.Json), auditId, command.RequestId, command.CorrelationId, previousVersion, quote.ResourceVersion, now));
        persistence.AddOutbox(new(eventId, trusted.WorkspaceId, quoteId, "QUOTE_CUSTOMER_RESPONSE_RECORDED", command.CorrelationId,
            JsonSerializer.Serialize(new { workspaceId = trusted.WorkspaceId, quoteId, previousVersion, resourceVersion = quote.ResourceVersion,
                record = JsonSerializer.Deserialize<QuoteCustomerDecisionReadModel>(record.PayloadJson, QuotePricing.Json), snapshot }, QuotePricing.Json), now));
        try { await persistence.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (QuotePersistenceConcurrencyException) { return Error("VERSION_CONFLICT", 412, "Resource version conflict"); }
        return Success(result, access.Value);
    }

    internal static QuoteOperationError? Validate(RecordQuoteCustomerDecisionRequest request, QuoteCommandMetadata command, DateTimeOffset now)
    {
        var fields = new Dictionary<string, string[]>();
        if (!QuotePricing.EntityId(request.DecisionId)) fields["decisionId"] = ["Valid decision identity required."];
        if (request.EvidenceType != "USER_REPORTED_ACCEPTANCE") fields["evidenceType"] = ["Only an unverified employee report may be recorded."];
        if (request.SourceChannel is not ("EMAIL" or "GMAIL" or "ZALO" or "CHAT_APP" or "SMS" or "OTHER")) fields["sourceChannel"] = ["Invalid human response channel."];
        QuotePricing.Text(request.SourceDescription, "sourceDescription", 2000, true, fields);
        QuotePricing.Text(request.Note, "note", 2000, false, fields);
        if (command.ExpectedVersion is null) fields["If-Match"] = ["A resource version is required."];
        if (request.ReportedCustomerDecisionAt is not null && (!DateTimeOffset.TryParseExact(request.ReportedCustomerDecisionAt,
            ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"], CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var reported) || reported > now)) fields["reportedCustomerDecisionAt"] = ["Reported time must be UTC-Z and no later than server time."];
        return fields.Count == 0 ? null : QuoteErrors.Validation(fields);
    }
    private static QuoteOperationResult<QuoteMutationResponse> Error(string code, int status, string title, string? detail = null) => Fail(new(code, status, title, detail));
    private static QuoteOperationResult<QuoteMutationResponse> Fail(QuoteOperationError error) => QuoteOperationResult<QuoteMutationResponse>.Failure(error);
    private static QuoteOperationResult<QuoteMutationResponse> Success(QuoteMutationResponse response, QuoteAccess access) =>
        QuoteOperationResult<QuoteMutationResponse>.Success(response with { Result = new(QuoteFieldSecurity.Project(response.Result.Quote, access.Authorization)) });
}
