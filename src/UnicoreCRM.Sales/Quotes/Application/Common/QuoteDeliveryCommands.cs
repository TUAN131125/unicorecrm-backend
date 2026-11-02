using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteDeliveryCommands(QuoteAuthorization authorization, IQuotesPersistence persistence,
    IQuoteWorkspacePolicyReader workspacePolicy, IQuoteApprovalPolicyReader policies,
    IWorkspaceTimeZoneReader timeZones, TimeProvider clock, IQuoteCustomerDecisionPolicyReader? customerPolicy = null, QuoteLifecycleClock? lifecycleClock = null, QuoteSalesPolicyReader? salesPolicies = null)
{
    internal static readonly string[] WriteFields = ["status", "sentAt", "deliveryHistory"];
    internal static readonly string[] ReadFields = ["buyerRef", "sourcePath", "sourceDealId", "contactId", "sourceLeadId",
        "title", "currency", "recipientEmail", "lineItems", "adjustments", "subtotal", "discountTotal", "taxTotal",
        "grandTotal", "validUntil", "notes", "paymentAgreement", "senderName", "senderAddress", "senderEmail", "senderTaxId",
        "deliveryHistory"];
    private const string Operation = "recordQuoteSendEvidenceCommand";

    internal async Task<QuoteOperationResult<QuoteMutationResponse>> ExecuteAsync(string quoteId,
        RecordQuoteSendEvidenceRequest request, QuoteCommandMetadata command, CancellationToken ct)
    {
        // Retained constructor compatibility for historical consumers; B1 is not an issuance prerequisite.
        _ = policies;
        _ = salesPolicies;
        var metadata = new QuoteRequestMetadata(command.RequestId, command.CorrelationId);
        var read = await authorization.AuthorizeAsync(metadata, ct);
        if (!read.IsSuccess) return Fail(read.Error!);
        var access = await authorization.AuthorizeAsync(metadata, ct, QuoteCapabilities.Update);
        if (!access.IsSuccess) return Fail(access.Error!);
        var trusted = access.Value!.Trusted;
        if (!QuotePricing.EntityId(quoteId)) return Fail(QuoteErrors.NotFound());
        var validation = Validate(request, (lifecycleClock?.GetUtcNow(trusted.WorkspaceId) ?? clock.GetUtcNow()));
        if (validation is not null) return Fail(validation);
        if (command.ExpectedVersion is null) return Fail(QuoteErrors.Validation(new Dictionary<string, string[]> { ["If-Match"] = ["Version required."] }));
        var payloadHash = QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new { quoteId, request, actor = trusted.MemberId }, QuotePricing.Json));
        var fingerprint = QuoteApprovalAssessment.Hash(payloadHash + "\n" + command.ExpectedVersion);
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
        if (WriteFields.Any(field => !access.Value.Authorization.CanWrite(field))
            || ReadFields.Any(field => !access.Value.Authorization.CanRead(field) || !read.Value!.Authorization.CanRead(field)))
            return Fail(QuoteErrors.AccessDenied());
        var replay = await persistence.FindCommandAsync(scope, ct);
        if (replay is not null)
        {
            if (replay.Fingerprint != fingerprint) return Error("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused");
            var response = JsonSerializer.Deserialize<QuoteMutationResponse>(replay.ResponseJson, QuotePricing.Json)
                ?? throw new InvalidOperationException("Invalid delivery receipt.");
            return Success(response with { Outcome = "REPLAYED", Result = new(QuoteProjection.Document(quote, customerPolicy?.Read(quote.WorkspaceId))) }, access.Value);
        }
        var history = quote.DeliveryHistoryJson is null ? new JsonArray() : JsonNode.Parse(quote.DeliveryHistoryJson)!.AsArray();
        var existing = history.OfType<JsonObject>().SingleOrDefault(item => item["id"]?.GetValue<string>() == request.DeliveryId);
        if (existing is not null)
        {
            if (existing["requestFingerprint"]?.GetValue<string>() != payloadHash)
                return Error("QUOTE_DELIVERY_EVIDENCE_INVALID", 409, "Delivery identity already used", "DELIVERY_ID_REUSED");
            // Receipt is historical; projection and authorization always reflect the current resource.
            var receipt = new QuoteMutationResponse(existing["commandId"]!.GetValue<string>(), command.CorrelationId, quoteId, "QUOTE",
                existing["resultVersion"]!.GetValue<long>(), existing["recordedAt"]!.GetValue<string>(), "REPLAYED",
                new(QuoteProjection.Document(quote, customerPolicy?.Read(quote.WorkspaceId))), [], [existing["eventId"]!.GetValue<string>()], [existing["auditId"]!.GetValue<string>()]);
            // Bind a newly presented key even when DeliveryId already identifies a commitment.
            // This immutable replay receipt changes neither Quote version nor delivery/outbox history.
            persistence.AddCommand(new(scope, trusted.WorkspaceId, trusted.MemberId, Operation, quoteId, fingerprint,
                JsonSerializer.Serialize(receipt, QuotePricing.Json), "quote_audit_" + Guid.NewGuid().ToString("N"),
                command.RequestId, command.CorrelationId, quote.ResourceVersion, quote.ResourceVersion, (lifecycleClock?.GetUtcNow(trusted.WorkspaceId) ?? clock.GetUtcNow())));
            await persistence.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Success(receipt, access.Value);
        }
        if (quote.ResourceVersion != command.ExpectedVersion) return Error("VERSION_CONFLICT", 412, "Resource version conflict");
        if (quote.Status is not ("DRAFT" or "REVIEW" or "SENT") || quote.ArchivedAt is not null)
            return Error("QUOTE_SEND_BLOCKED", 409, "Quote cannot be sent", "QUOTE_STATE_INVALID");
        var workspace = await workspacePolicy.ReadAsync(trusted.WorkspaceId, ct);
        if (workspace is null || !workspace.Enabled) return Fail(QuoteErrors.AccessDenied());
        var now = (lifecycleClock?.GetUtcNow(trusted.WorkspaceId) ?? clock.GetUtcNow());
        var timezone = await timeZones.ReadTimeZoneAsync(trusted.WorkspaceId, ct);
        var dateError = ValidateExpiry(quote.ValidUntil, timezone, now);
        if (dateError is not null) return Fail(dateError);
        // The reported time cannot acquire authority while waiting for an aggregate lock.
        validation = Validate(request, now);
        if (validation is not null) return Fail(validation);
        if (request.Channel is "EMAIL" or "GMAIL" && !string.IsNullOrWhiteSpace(quote.RecipientEmail)
            && !string.Equals(quote.RecipientEmail, request.RecipientEmail, StringComparison.OrdinalIgnoreCase))
            return Fail(QuoteErrors.Validation(new Dictionary<string, string[]> { ["recipientEmail"] = ["Recipient must match the saved Quote."] }));
        var commercialFingerprint = QuoteApprovalAssessment.ContentFingerprint(quote);
        var previousVersion = quote.ResourceVersion;
        var snapshot = QuoteProjection.Document(quote) with { DeliveryHistory = null, CustomerDecisionHistory = null };
        var eventId = "quote_event_" + Guid.NewGuid().ToString("N");
        var auditId = "quote_audit_" + Guid.NewGuid().ToString("N");
        var commandId = "quote_command_" + Guid.NewGuid().ToString("N");
        var recordedAt = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        var record = new QuoteDeliveryRecordReadModel(request.DeliveryId, request.Channel, recordedAt, commercialFingerprint)
        {
            EvidenceType = "USER_CONFIRMED_SENT", RecipientEmail = request.RecipientEmail, Recipient = request.Recipient,
            Note = request.Note, SentBy = trusted.MemberId, FileName = request.FileName, RecordedAt = recordedAt,
            ReportedSentAt = request.SentAt, QuoteRevision = quote.QuoteRevision, ResourceVersion = previousVersion,
            ApprovalContentFingerprint = commercialFingerprint, ApprovalPolicyVersion = QuoteDirectIssuancePolicy.Version,
            DocumentAuthority = "COMMERCIAL_CONTENT_ONLY"
        };
        var stored = JsonSerializer.SerializeToNode(record, QuotePricing.Json)!.AsObject();
        stored["requestFingerprint"] = payloadHash; stored["commandId"] = commandId;
        stored["eventId"] = eventId; stored["auditId"] = auditId; stored["resultVersion"] = previousVersion + 1;
        history.Add(stored);
        if (!quote.RecordDelivery(history.ToJsonString(QuotePricing.Json), now)) throw new InvalidOperationException("Validated Quote refused delivery.");
        var result = new QuoteMutationResponse(commandId, command.CorrelationId, quoteId, "QUOTE", quote.ResourceVersion,
            recordedAt, "COMMITTED", new(QuoteProjection.Document(quote, customerPolicy?.Read(quote.WorkspaceId))), [], [eventId], [auditId]);
        persistence.AddCommand(new(scope, trusted.WorkspaceId, trusted.MemberId, Operation, quoteId, fingerprint,
            JsonSerializer.Serialize(result, QuotePricing.Json), auditId, command.RequestId, command.CorrelationId, previousVersion, quote.ResourceVersion, now));
        persistence.AddOutbox(new(eventId, trusted.WorkspaceId, quoteId, "QUOTE_DELIVERY_RECORDED", command.CorrelationId,
            JsonSerializer.Serialize(new { workspaceId = trusted.WorkspaceId, quoteId, previousVersion, resourceVersion = quote.ResourceVersion,
                record, snapshot, actor = trusted.MemberId }, QuotePricing.Json), now));
        try { await persistence.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (QuotePersistenceConcurrencyException) { return Error("VERSION_CONFLICT", 412, "Resource version conflict"); }
        return Success(result, access.Value);
    }

    internal static QuoteOperationError? Validate(RecordQuoteSendEvidenceRequest request, DateTimeOffset now)
    {
        var fields = new Dictionary<string, string[]>();
        if (!QuotePricing.EntityId(request.DeliveryId)) fields["deliveryId"] = ["Valid delivery identity required."];
        if (request.Channel is not ("EMAIL" or "GMAIL" or "ZALO" or "CHAT_APP" or "SMS" or "OTHER" or "PDF")) fields["channel"] = ["Unsupported channel."];
        if (request.EvidenceType != "USER_CONFIRMED_SENT") return new("QUOTE_DELIVERY_EVIDENCE_INVALID", 422, "Only authenticated user confirmation is supported", "PROVIDER_EVIDENCE_UNVERIFIED");
        if (request.Channel is "EMAIL" or "GMAIL")
        {
            if (request.RecipientEmail is not { Length: > 0 and <= 320 } email || email.Any(char.IsControl)
                || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email) fields["recipientEmail"] = ["Valid recipient email required."];
        }
        else if (string.IsNullOrWhiteSpace(request.Recipient)) fields["recipient"] = ["Recipient/destination required."];
        foreach (var (name, value, limit) in new[] { ("recipient", request.Recipient, 320), ("recipientEmail", request.RecipientEmail, 320), ("note", request.Note, 2000), ("fileName", request.FileName, 300) })
            if (value is not null && (value.Length > limit || value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))) fields[name] = ["Invalid field value."];
        if (request.SentAt is not null && (!DateTimeOffset.TryParseExact(request.SentAt,
            ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var reported) || reported > now)) fields["sentAt"] = ["Reported time must be a valid timestamp no later than server time."];
        return fields.Count == 0 ? null : QuoteErrors.Validation(fields);
    }

    internal static QuoteOperationError? ValidateExpiry(DateOnly? validUntil, string? timezone, DateTimeOffset now)
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timezone ?? ""); }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { return new("QUOTE_SEND_BLOCKED", 409, "Workspace timezone unavailable", "WORKSPACE_TIMEZONE_INVALID"); }
        return validUntil is { } date && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime) > date
            ? new("QUOTE_SEND_BLOCKED", 409, "Quote has expired", "QUOTE_EXPIRED") : null;
    }
    private static QuoteOperationResult<QuoteMutationResponse> Error(string code, int status, string title, string? detail = null) => Fail(new(code, status, title, detail));
    private static QuoteOperationResult<QuoteMutationResponse> Fail(QuoteOperationError error) => QuoteOperationResult<QuoteMutationResponse>.Failure(error);
    private static QuoteOperationResult<QuoteMutationResponse> Success(QuoteMutationResponse response, QuoteAccess access) =>
        QuoteOperationResult<QuoteMutationResponse>.Success(response with { Result = new(QuoteFieldSecurity.Project(response.Result.Quote, access.Authorization)) });
}
