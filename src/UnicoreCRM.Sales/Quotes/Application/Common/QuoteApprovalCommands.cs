using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteApprovalCommands(QuoteAuthorization authorization, IQuotesPersistence persistence,
    IQuoteWorkspacePolicyReader workspacePolicy, IQuoteApprovalPolicyReader policies,
    IQuoteApprovalAvailability availability, TimeProvider clock)
{
    internal static readonly string[] ChangedFields = ["status", "reviewRequestedAt", "approvalStatus", "approvalRequired",
        "approvalReasons", "approvalRequestedAt", "approvalRequestedBy", "approvedAt", "approvedBy",
        "approvalDecisionNote", "approvalContentFingerprint", "approvalPolicyVersion"];
    private static readonly string[] CommercialFields = ["buyerRef", "sourcePath", "sourceDealId", "contactId", "sourceLeadId",
        "title", "currency", "recipientEmail", "lineItems", "adjustments", "subtotal", "discountTotal", "taxTotal",
        "grandTotal", "validUntil", "notes", "paymentAgreement", "senderName", "senderAddress", "senderEmail", "senderTaxId"];

    internal async Task<QuoteOperationResult<QuoteMutationResponse>> ExecuteAsync(string operation, string quoteId,
        string? note, QuoteCommandMetadata command, CancellationToken ct)
    {
        var metadata = new QuoteRequestMetadata(command.RequestId, command.CorrelationId);
        var requesting = operation == "requestQuoteApprovalCommand";
        var staleCode = operation == "requestQuoteApprovalChangesCommand" ? "QUOTE_APPROVAL_NOT_PENDING" : "QUOTE_APPROVAL_STALE";
        if (!requesting && operation is not ("approveQuoteCommand" or "requestQuoteApprovalChangesCommand"))
            throw new ArgumentOutOfRangeException(nameof(operation));
        var access = await authorization.AuthorizeAsync(metadata, ct, requesting ? QuoteCapabilities.Update : QuoteCapabilities.Approve);
        if (!access.IsSuccess) return Fail(access.Error!);
        var trusted = access.Value!.Trusted;
        if (!QuotePricing.EntityId(quoteId)) return Fail(QuoteErrors.NotFound());
        if (command.ExpectedVersion is null || note?.Length > 2000 || operation == "requestQuoteApprovalChangesCommand" && string.IsNullOrWhiteSpace(note))
            return Fail(QuoteErrors.Validation(new Dictionary<string, string[]> { ["command"] = ["Version and a valid decision note are required."] }));
        var fingerprint = QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new { operation, quoteId, note, command.ExpectedVersion }, QuotePricing.Json));
        var scope = QuoteApprovalAssessment.Hash($"{trusted.WorkspaceId}\n{trusted.MemberId}\n{operation}\n{quoteId}\n{command.IdempotencyKey}");
        await using var transaction = await persistence.BeginAsync(ct);
        await persistence.LockAsync(scope, ct);
        await persistence.LockAsync($"aggregate:{trusted.WorkspaceId}:{quoteId}", ct);
        var quote = await persistence.LoadQuoteAsync(trusted.WorkspaceId, quoteId, ct);
        if (quote is null) return Fail(QuoteErrors.NotFound());
        var denied = await authorization.EnforceRecordAsync(access.Value, quote, operation, metadata, ct);
        if (denied is not null) return Fail(denied);
        var replay = await persistence.FindCommandAsync(scope, ct);
        if (replay is not null)
        {
            if (replay.Fingerprint != fingerprint) return Error("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused");
            var stored = JsonSerializer.Deserialize<QuoteMutationResponse>(replay.ResponseJson, QuotePricing.Json)
                ?? throw new InvalidOperationException("Invalid Quote approval command evidence.");
            return Success(stored with { Outcome = "REPLAYED" }, access.Value);
        }
        if (quote.ResourceVersion != command.ExpectedVersion) return Error("VERSION_CONFLICT", 412, "Resource version conflict");
        if (quote.Status is not ("DRAFT" or "REVIEW") || quote.ArchivedAt is not null)
            return Error(staleCode, 409, "Quote is no longer mutable");
        if (ChangedFields.Any(field => !access.Value.Authorization.CanWrite(field))
            || CommercialFields.Any(field => !access.Value.Authorization.CanRead(field))) return Fail(QuoteErrors.AccessDenied());
        var workspace = await workspacePolicy.ReadAsync(trusted.WorkspaceId, ct);
        if (workspace is null || !workspace.Enabled) return Fail(QuoteErrors.AccessDenied());
        var policy = await policies.ReadAsync(trusted.WorkspaceId, ct);
        if (policy is null) return Error("INTEGRATION_UNAVAILABLE", 503, "Workspace approval policy is unavailable", "APPROVAL_POLICY_MISSING_OR_INVALID");
        var creator = await persistence.ReadCreatorAsync(trusted.WorkspaceId, quoteId, ct);
        if (creator is null) return Error(staleCode, 409, "Trusted creator evidence is unavailable", "LEGACY_CREATOR_EVIDENCE_MISSING");
        var assessment = QuoteApprovalAssessment.Evaluate(quote, policy);
        if (!requesting)
        {
            if (trusted.MemberId == creator || trusted.MemberId == quote.ApprovalRequestedBy)
                return Error("ACCESS_DENIED", 403, "Independent approval is required", "SELF_APPROVAL_FORBIDDEN");
            if (quote.ApprovalStatus != "PENDING" || quote.ApprovalRequestedBy is null || quote.ApprovalRequestedAt is null)
                return Error("QUOTE_APPROVAL_NOT_PENDING", 409, "Quote approval is not pending");
            if (quote.ApprovalContentFingerprint != assessment.Fingerprint || quote.ApprovalPolicyVersion != assessment.PolicyVersion)
                return Error(staleCode, 409, "Quote content or Workspace approval policy changed");
        }
        else if (assessment.Required && !await availability.HasEligibleApproverAsync(trusted.WorkspaceId,
            quote.OwnerId, creator, trusted.MemberId, ChangedFields, CommercialFields, ct))
            return Error("QUOTE_APPROVAL_REQUIRED", 409, "No eligible independent approver is available", "NO_ELIGIBLE_APPROVER");
        var previousVersion = quote.ResourceVersion;
        var now = clock.GetUtcNow();
        if (requesting) quote.RequestApproval(assessment.Required, JsonSerializer.Serialize(assessment.Reasons, QuotePricing.Json),
            assessment.Fingerprint, assessment.PolicyVersion, trusted.MemberId, note, now);
        else quote.DecideApproval(operation == "approveQuoteCommand", trusted.MemberId, note, now);
        var eventId = "quote_event_" + Guid.NewGuid().ToString("N");
        var auditId = "quote_audit_" + Guid.NewGuid().ToString("N");
        var response = new QuoteMutationResponse("quote_command_" + Guid.NewGuid().ToString("N"), command.CorrelationId,
            quoteId, "QUOTE", quote.ResourceVersion, now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), "COMMITTED",
            new(QuoteProjection.Document(quote)), [], [eventId], [auditId]);
        persistence.AddCommand(new(scope, trusted.WorkspaceId, trusted.MemberId, operation, quoteId,
            fingerprint, JsonSerializer.Serialize(response, QuotePricing.Json), auditId, command.RequestId,
            command.CorrelationId, previousVersion, quote.ResourceVersion, now));
        var eventType = requesting ? "QUOTE_APPROVAL_REQUESTED" : operation == "approveQuoteCommand" ? "QUOTE_APPROVED" : "QUOTE_APPROVAL_CHANGES_REQUESTED";
        persistence.AddOutbox(new(eventId, trusted.WorkspaceId, quoteId, eventType, command.CorrelationId,
            JsonSerializer.Serialize(new { quoteId, quote.ResourceVersion, assessment.Fingerprint, assessment.PolicyVersion,
                creator, requester = quote.ApprovalRequestedBy, actor = trusted.MemberId, note }, QuotePricing.Json), now));
        try { await persistence.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (QuotePersistenceConcurrencyException) { return Error("VERSION_CONFLICT", 412, "Resource version conflict"); }
        return Success(response, access.Value);
    }
    private static QuoteOperationResult<QuoteMutationResponse> Error(string code, int status, string title, string? detail = null) => Fail(new(code, status, title, detail));
    private static QuoteOperationResult<QuoteMutationResponse> Fail(QuoteOperationError error) => QuoteOperationResult<QuoteMutationResponse>.Failure(error);
    private static QuoteOperationResult<QuoteMutationResponse> Success(QuoteMutationResponse response, QuoteAccess access) =>
        QuoteOperationResult<QuoteMutationResponse>.Success(response with { Result = new(QuoteFieldSecurity.Project(response.Result.Quote, access.Authorization)) });
}
