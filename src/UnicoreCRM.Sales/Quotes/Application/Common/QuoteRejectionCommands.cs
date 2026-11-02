using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteRejectionCommands(QuoteAuthorization authorization, IQuotesPersistence persistence, TimeProvider clock,
    IQuoteCustomerDecisionPolicyReader policies, IQuoteWorkspacePolicyReader workspacePolicy, QuoteLifecycleClock? lifecycleClock = null)
{
    internal static readonly string[] ChangedFields = ["status", "rejectedAt", "customerDecisionHistory"];
    private const string Operation = "rejectQuoteCommand";

    internal async Task<QuoteOperationResult<QuoteMutationResponse>> ExecuteAsync(string quoteId, RejectQuoteRequest request,
        QuoteCommandMetadata command, CancellationToken ct)
    {
        var metadata = new QuoteRequestMetadata(command.RequestId, command.CorrelationId);
        var read = await authorization.AuthorizeAsync(metadata, ct);
        if (!read.IsSuccess) return Fail(read.Error!);
        var access = await authorization.AuthorizeAsync(metadata, ct, QuoteCapabilities.Update);
        if (!access.IsSuccess) return Fail(access.Error!);
        if (!QuotePricing.EntityId(quoteId)) return Fail(QuoteErrors.NotFound());
        var fields = new Dictionary<string, string[]>();
        QuotePricing.Text(request.Reason, "reason", 500, true, fields);
        QuotePricing.Text(request.Note, "note", 2000, false, fields);
        if (command.ExpectedVersion is null) fields["If-Match"] = ["A resource version is required."];
        if (fields.Count != 0) return Fail(QuoteErrors.Validation(fields));
        var trusted = access.Value!.Trusted;
        var fingerprint = QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new { quoteId, request, command.ExpectedVersion }, QuotePricing.Json));
        var scope = QuoteApprovalAssessment.Hash($"{trusted.WorkspaceId}\n{trusted.MemberId}\n{Operation}\n{quoteId}\n{command.IdempotencyKey}");
        await using var transaction = await persistence.BeginAsync(ct);
        await persistence.LockAsync(scope, ct);
        await persistence.LockAsync($"aggregate:{trusted.WorkspaceId}:{quoteId}", ct);
        var quote = await persistence.LoadQuoteAsync(trusted.WorkspaceId, quoteId, ct);
        if (quote is null) return Fail(QuoteErrors.NotFound());
        foreach (var grant in new[] { read.Value!, access.Value })
        {
            var denied = await authorization.EnforceRecordAsync(grant, quote, Operation, metadata, ct);
            if (denied is not null) return Fail(denied);
        }
        if (ChangedFields.Any(field => !access.Value.Authorization.CanWrite(field))
            || !access.Value.Authorization.CanRead("customerDecisionHistory") || !read.Value!.Authorization.CanRead("customerDecisionHistory")
            || QuoteDeliveryCommands.ReadFields.Any(field => !access.Value.Authorization.CanRead(field) || !read.Value!.Authorization.CanRead(field)))
            return Fail(QuoteErrors.AccessDenied());
        var workspace = await workspacePolicy.ReadAsync(trusted.WorkspaceId, ct);
        if (workspace is null || !workspace.Enabled) return Fail(QuoteErrors.AccessDenied());
        var policy = policies.Read(trusted.WorkspaceId);
        if (policy is null) return Error("INTEGRATION_UNAVAILABLE", 503, "Customer decision policy unavailable", "CUSTOMER_DECISION_POLICY_MISSING_OR_INVALID");
        if (!policy.AllowRejection) return Error("QUOTE_REJECTION_BLOCKED", 409, "Rejection disabled by Workspace policy", "CUSTOMER_DECISION_POLICY_DISABLED");
        var replay = await persistence.FindCommandAsync(scope, ct);
        if (replay is not null)
        {
            if (replay.Fingerprint != fingerprint) return Error("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused");
            var committed = JsonSerializer.Deserialize<QuoteMutationResponse>(replay.ResponseJson, QuotePricing.Json)
                ?? throw new InvalidOperationException("Invalid Quote rejection receipt.");
            return Success(committed with { Outcome = "REPLAYED", Result = new(QuoteProjection.Document(quote, policy)) }, access.Value);
        }
        if (quote.ResourceVersion != command.ExpectedVersion) return Error("VERSION_CONFLICT", 412, "Resource version conflict");
        var snapshot = QuoteProjection.Document(quote) with { CustomerDecisionHistory = null, DeliveryHistory = null };
        var commercialFingerprint = QuoteApprovalAssessment.ContentFingerprint(quote);
        var previousVersion = quote.ResourceVersion;
        var now = lifecycleClock?.GetUtcNow(trusted.WorkspaceId) ?? clock.GetUtcNow();
        var eventId = "quote_event_" + Guid.NewGuid().ToString("N");
        var auditId = "quote_audit_" + Guid.NewGuid().ToString("N");
        var commandId = "quote_command_" + Guid.NewGuid().ToString("N");
        var record = QuoteCustomerDecisionRecords.Create(quote, "quote_decision_" + Guid.NewGuid().ToString("N"), trusted.MemberId,
            "REJECTION", "EMPLOYEE_RECORDED_REJECTION", request.Reason, null, request.Reason, request.Note, policy,
            fingerprint, commandId, auditId, eventId, now);
        if (!quote.Reject(request.Reason, now, record)) return Error("QUOTE_REJECTION_BLOCKED", 409, "Quote cannot be rejected", "QUOTE_STATE_INVALID");
        var response = new QuoteMutationResponse(commandId, command.CorrelationId, quoteId,
            "QUOTE", quote.ResourceVersion, now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), "COMMITTED",
            new(QuoteProjection.Document(quote, policy)), [], [eventId], [auditId]);
        persistence.AddCommand(new(scope, trusted.WorkspaceId, trusted.MemberId, Operation, quoteId, fingerprint,
            JsonSerializer.Serialize(response, QuotePricing.Json), auditId, command.RequestId, command.CorrelationId,
            previousVersion, quote.ResourceVersion, now));
        persistence.AddOutbox(new(eventId, trusted.WorkspaceId, quoteId, "QUOTE_REJECTED", command.CorrelationId,
            JsonSerializer.Serialize(new { workspaceId = trusted.WorkspaceId, quoteId, quote.QuoteRevision, previousVersion,
                resourceVersion = quote.ResourceVersion, commercialFingerprint, buyerRef = snapshot.BuyerRef,
                reason = request.Reason, request.Note, actor = trusted.MemberId, recordedAt = response.OccurredAt,
                source = "AUTHENTICATED_EMPLOYEE_REPORT", verificationStatus = "UNVERIFIED", record = JsonSerializer.Deserialize<QuoteCustomerDecisionReadModel>(record.PayloadJson, QuotePricing.Json), snapshot }, QuotePricing.Json), now));
        try { await persistence.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (QuotePersistenceConcurrencyException) { return Error("VERSION_CONFLICT", 412, "Resource version conflict"); }
        return Success(response, access.Value);
    }
    private static QuoteOperationResult<QuoteMutationResponse> Error(string code, int status, string title, string? detail = null) => Fail(new(code, status, title, detail));
    private static QuoteOperationResult<QuoteMutationResponse> Fail(QuoteOperationError error) => QuoteOperationResult<QuoteMutationResponse>.Failure(error);
    private static QuoteOperationResult<QuoteMutationResponse> Success(QuoteMutationResponse response, QuoteAccess access) =>
        QuoteOperationResult<QuoteMutationResponse>.Success(response with { Result = new(QuoteFieldSecurity.Project(response.Result.Quote, access.Authorization)) });
}
