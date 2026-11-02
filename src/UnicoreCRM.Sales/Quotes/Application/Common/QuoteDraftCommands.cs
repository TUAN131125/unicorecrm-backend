using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteDraftCommands(QuoteAuthorization authorization, IQuotesPersistence persistence,
    IQuoteRelationshipVerifier relationships, IQuoteProductSnapshotReader products,
    IWorkspaceMemberReferenceValidator members, IQuoteWorkspacePolicyReader workspacePolicy, TimeProvider clock)
{
    internal async Task<QuoteOperationResult<QuoteMutationResponse>> ExecuteAsync(QuoteDraftRequest request,
        QuoteCommandMetadata command, string? quoteId, string? dealId, CancellationToken ct)
    {
        var metadata = new QuoteRequestMetadata(command.RequestId, command.CorrelationId);
        var updating = quoteId is not null;
        var operation = updating ? "updateQuoteDraftCommand" : dealId is null ? "createQuoteCommand" : "createQuoteForDeal";
        var access = await authorization.AuthorizeAsync(metadata, ct, updating ? QuoteCapabilities.Update : QuoteCapabilities.Create);
        if (!access.IsSuccess) return Fail(access.Error!);
        var trusted = access.Value!.Trusted;
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        QuotePricing.Text(request.Title, "title", 300, true, fields);
        QuotePricing.Text(request.Notes, "notes", 4000, false, fields);
        if (request.RecipientEmail is not null && (request.RecipientEmail.Length > 320 || !MailAddress.TryCreate(request.RecipientEmail, out _))) fields["recipientEmail"] = ["Invalid email address."];
        DateOnly? validUntil = null;
        if (request.ValidUntil is not null)
        {
            if (DateOnly.TryParseExact(request.ValidUntil, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) validUntil = date;
            else fields["validUntil"] = ["Invalid business date."];
        }
        if (quoteId is not null && !QuotePricing.EntityId(quoteId) || dealId is not null && !QuotePricing.EntityId(dealId)) return Fail(QuoteErrors.NotFound());
        if (updating || dealId is not null)
        { if (command.ExpectedVersion is null) fields["If-Match"] = ["A resource version is required."]; }
        if (fields.Count != 0) return Fail(QuoteErrors.Validation(fields));
        var fingerprint = Hash(JsonSerializer.Serialize(new { request, command.ExpectedVersion, quoteId, dealId }, QuotePricing.Json));
        var scope = Hash($"{trusted.WorkspaceId}\n{trusted.MemberId}\n{operation}\n{quoteId ?? dealId ?? "WORKSPACE"}\n{command.IdempotencyKey}");
        await using var transaction = await persistence.BeginAsync(ct);
        await persistence.LockAsync(scope, ct);
        if (quoteId is not null) await persistence.LockAsync($"aggregate:{trusted.WorkspaceId}:{quoteId}", ct);
        Quote? existingQuote = null;
        if (quoteId is not null)
        {
            existingQuote = await persistence.LoadQuoteAsync(trusted.WorkspaceId, quoteId, ct);
            if (existingQuote is null) return Fail(QuoteErrors.NotFound());
            var denied = await authorization.EnforceRecordAsync(access.Value, existingQuote, operation, metadata, ct);
            if (denied is not null) return Fail(denied);
        }
        var replay = await persistence.FindCommandAsync(scope, ct);
        if (replay is not null)
        {
            var current = existingQuote ?? await persistence.ReadQuoteAsync(trusted.WorkspaceId, replay.QuoteId, ct);
            if (current is null) return Fail(QuoteErrors.NotFound());
            var denied = await authorization.EnforceRecordAsync(access.Value, current, operation, metadata, ct);
            if (denied is not null) return Fail(denied);
            if (replay.Fingerprint != fingerprint) return Fail(new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused"));
            var result = JsonSerializer.Deserialize<QuoteMutationResponse>(replay.ResponseJson, QuotePricing.Json)
                ?? throw new InvalidOperationException("Invalid persisted Quote command response.");
            // Preserve the committed instant while projecting old offset-formatted local fixture
            // evidence into the adopted UTC wire format. Never rewrite immutable stored evidence.
            var occurredAt = DateTimeOffset.Parse(result.OccurredAt, CultureInfo.InvariantCulture).UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            return Success(result with { Outcome = "REPLAYED", OccurredAt = occurredAt }, access.Value);
        }
        if (existingQuote is not null && existingQuote.ResourceVersion != command.ExpectedVersion)
            return Fail(new("VERSION_CONFLICT", 412, "Resource version conflict"));
        if (existingQuote is not null && (existingQuote.Status is not ("DRAFT" or "REVIEW") || existingQuote.ArchivedAt is not null))
            return Fail(new("QUOTE_DRAFT_IMMUTABLE", 409, "Quote content is immutable"));
        var policy = await workspacePolicy.ReadAsync(trusted.WorkspaceId, ct);
        if (policy is null || !policy.Enabled) return Fail(QuoteErrors.AccessDenied());
        if (request.Currency is null || !policy.EnabledCurrencies.Contains(request.Currency, StringComparer.Ordinal)) fields["currency"] = ["Currency is not enabled in this Workspace."];
        var path = existingQuote?.SourcePath ?? (dealId is null ? request.SourcePath : "DEAL");
        var sourceDealId = existingQuote?.SourceDealId ?? dealId;
        if (!updating && dealId is null && (path != "DIRECT_SALE" || request.SourceDealId is not null)) fields["sourcePath"] = ["Use the Deal Quote command for a verified Deal source."];
        if (dealId is not null && (request.BuyerRef is not null || request.SourcePath is not null || request.SourceDealId is not null)) fields["sourcePath"] = ["Deal source and buyer are derived by the server."];
        IQuoteDealSourceLease? sourceLease = null;
        try
        {
            if (sourceDealId is not null)
            {
                sourceLease = await relationships.AcquireDealAsync(sourceDealId, updating ? null : command.ExpectedVersion, !updating, metadata.RequestId, metadata.CorrelationId, ct);
                if (sourceLease.ErrorCode is { } sourceError)
                    return Fail(new(sourceError, sourceError == "VERSION_CONFLICT" ? 412 : sourceError == "RESOURCE_NOT_FOUND" ? 404 : sourceError == "ACCESS_DENIED" ? 403 : sourceError == "IDEMPOTENCY_REQUEST_IN_PROGRESS" ? 409 : 422, "Deal source cannot be used"));
            }
            var source = sourceLease?.Source;
            var buyer = existingQuote is not null ? new QuoteBuyerReference(existingQuote.BuyerType, existingQuote.BuyerId) : source?.BuyerRef ?? request.BuyerRef;
            var contactId = existingQuote is not null ? existingQuote.ContactId : source?.ContactId ?? request.ContactId;
            var leadId = existingQuote is not null ? existingQuote.SourceLeadId : source?.SourceLeadId;
            if (buyer is null || buyer.Type is not ("CONTACT" or "ORGANIZATION_ACCOUNT") || !QuotePricing.EntityId(buyer.Id)) fields["buyerRef"] = ["A valid buyer is required."];
            else if (!await relationships.VerifyBuyerAsync(buyer, metadata.RequestId, metadata.CorrelationId, ct)) fields["buyerRef"] = ["Buyer is not available under current access."];
            if (source is not null && existingQuote is not null && source.BuyerRef != buyer) fields["buyerRef"] = ["Deal buyer has changed; existing Quote source cannot be silently replaced."];
            if (existingQuote is not null && (request.BuyerRef != buyer || request.SourcePath != existingQuote.SourcePath || request.SourceDealId != existingQuote.SourceDealId)) fields["sourcePath"] = ["Draft replacement cannot change source identity."];
            if (source is not null && (request.ContactId is not null && request.ContactId != source.ContactId || request.SourceLeadId is not null && request.SourceLeadId != source.SourceLeadId)) fields["sourceDealId"] = ["Source references do not match the Deal."];
            if (source is null && existingQuote is null)
            {
                if (request.SourceLeadId is not null) fields["sourceLeadId"] = ["Direct-sale Lead source verification is not available."];
                if (contactId is not null && (buyer?.Type != "CONTACT" || buyer.Id != contactId)) fields["contactId"] = ["Direct-sale Contact must be the buyer."];
            }
            if (existingQuote is not null && (request.ContactId != existingQuote.ContactId || request.SourceLeadId != existingQuote.SourceLeadId)) fields["sourcePath"] = ["Draft replacement cannot change source references."];
            var owner = request.OwnerId ?? existingQuote?.OwnerId ?? trusted.MemberId;
            if (!QuotePricing.EntityId(owner) || !await members.IsActiveMemberAsync(trusted.WorkspaceId, owner, ct)) fields["ownerId"] = ["Owner must be an active Workspace member."];
            if (access.Value.Authorization.ScopeFilter != RecordAccessScopeFilter.Workspace
                && (access.Value.Authorization.ScopeFilter != RecordAccessScopeFilter.OwnedByMember || owner != access.Value.Authorization.ScopeOwnerMemberId)) return Fail(QuoteErrors.AccessDenied());
            if (fields.Count != 0) return Fail(QuoteErrors.Validation(fields));
            var old = existingQuote is null ? null : QuoteProjection.Document(existingQuote);
            var initialPricing = QuotePricing.Calculate(request, fields, old?.LineItems);
            if (initialPricing is null || fields.Count != 0) return Fail(QuoteErrors.Validation(fields));
            var ids = request.LineItems!.Where(x => x?.ProductId is not null).Select(x => x!.ProductId!).Distinct(StringComparer.Ordinal).ToArray();
            var catalog = ids.Length == 0 ? new QuoteProductSnapshotResult(true, new Dictionary<string, QuoteProductSnapshot>())
                : await products.ReadAsync(ids, metadata.RequestId, metadata.CorrelationId, ct);
            if (!catalog.IsAuthorized) return Fail(QuoteErrors.AccessDenied());
            var verifiedLines = new List<QuoteDraftLineInput?>();
            var captures = new List<object>();
            var oldCaptures = new Dictionary<(int Index, string ProductId), JsonElement>();
            if (existingQuote?.ProductSnapshotsJson is not null)
            {
                using var stored = JsonDocument.Parse(existingQuote.ProductSnapshotsJson);
                foreach (var capture in stored.RootElement.EnumerateArray())
                    oldCaptures[(capture.GetProperty("index").GetInt32(), capture.GetProperty("productId").GetString()!)] = capture.GetProperty("snapshot").Clone();
            }
            for (var index = 0; index < request.LineItems!.Count; index++)
            {
                var line = request.LineItems[index]!;
                if (line.ProductId is not { } productId) { verifiedLines.Add(line); continue; }
                if (!catalog.Items.TryGetValue(productId, out var product)) { fields[$"lineItems[{index}].productId"] = ["Active Product is not available under current access."]; continue; }
                var prior = old is not null && index < old.LineItems.Count && old.LineItems[index].ProductId == productId ? old.LineItems[index] : null;
                var hasPriorCapture = oldCaptures.TryGetValue((index, productId), out var priorCaptureSnapshot);
                var preserve = hasPriorCapture && prior is not null && EqualMoney(line.UnitPrice!, prior.UnitPrice)
                    && SameNumber(line.TaxRate ?? "0", prior.TaxRate ?? "0") && line.TaxMode == prior.TaxMode;
                var authoritativePrice = preserve ? prior!.UnitPrice : new QuoteMoney(product.UnitPrice.Amount!, product.UnitPrice.Currency!);
                var taxRate = preserve ? prior!.TaxRate ?? "0" : product.TaxRate;
                var taxMode = preserve ? prior!.TaxMode : product.TaxMode.ToUpperInvariant();
                if (!EqualMoney(line.UnitPrice!, authoritativePrice) || !SameNumber(line.TaxRate ?? "0", taxRate) || line.TaxMode != taxMode)
                    fields[$"lineItems[{index}].unitPrice"] = ["Product pricing must match its authoritative snapshot; no price override policy is granted."];
                verifiedLines.Add(line with { ProductNameSnapshot = preserve ? prior!.ProductNameSnapshot : product.Name,
                    SkuSnapshot = preserve ? prior!.SkuSnapshot : product.Sku, ProductTypeSnapshot = preserve ? prior!.ProductTypeSnapshot : product.Type,
                    DescriptionSnapshot = preserve ? prior!.DescriptionSnapshot : product.Description,
                    BillingCycleSnapshot = preserve ? prior!.BillingCycleSnapshot : product.BillingCycle,
                    UnitPrice = authoritativePrice, TaxRate = taxRate, TaxMode = taxMode });
                // Original per-line evidence is retained when its captured commercial price is unchanged.
                object capturedSnapshot = product;
                if (preserve) capturedSnapshot = priorCaptureSnapshot;
                captures.Add(new { index, productId, preserved = preserve, snapshot = capturedSnapshot });
            }
            if (fields.Count != 0) return Fail(QuoteErrors.Validation(fields));
            var canonical = request with { LineItems = verifiedLines };
            var pricing = QuotePricing.Calculate(canonical, fields, old?.LineItems)!;
            quoteId ??= "quote_" + Guid.NewGuid().ToString("N");
            var payment = QuotePricing.Payment(request.PaymentAgreement, pricing, request.Currency!, quoteId, fields, old?.PaymentAgreement);
            if (fields.Count != 0) return Fail(QuoteErrors.Validation(fields));
            var content = new QuoteDraftContent(request.Title!.Trim(), request.Currency!, owner, request.RecipientEmail,
                Serialize(pricing.Lines), Serialize(pricing.Adjustments), Serialize(captures), pricing.Subtotal, pricing.Discount,
                pricing.Tax, pricing.Total, validUntil, request.Notes, payment is null ? null : Serialize(payment));
            var prospective = new Quote(trusted.WorkspaceId, quoteId, existingQuote?.QuoteNumber ?? "pending", buyer!.Type,
                buyer.Id, path!, sourceDealId, contactId, leadId, content, clock.GetUtcNow());
            var proposed = QuoteProjection.Document(prospective);
            var oldValues = old is null ? null : Values(old);
            foreach (var pair in Values(proposed))
                if ((oldValues is null && pair.Value != "null" || oldValues is not null && oldValues[pair.Key] != pair.Value)
                    && !access.Value.Authorization.CanWrite(pair.Key)) return Fail(QuoteErrors.AccessDenied());
            var now = clock.GetUtcNow();
            var priorVersion = existingQuote?.ResourceVersion;
            var invalidatedApproval = existingQuote?.ApprovalContentFingerprint is null ? null : new
            {
                existingQuote.ApprovalStatus, existingQuote.ApprovalContentFingerprint,
                existingQuote.ApprovalPolicyVersion, existingQuote.ApprovalRequestedBy, existingQuote.ApprovedBy
            };
            var quote = existingQuote;
            if (quote is null)
            {
                quote = new(trusted.WorkspaceId, quoteId, await persistence.NextNumberAsync(ct), buyer.Type, buyer.Id,
                    path!, sourceDealId, contactId, leadId, content, now);
                persistence.AddQuote(quote);
            }
            else if (!quote.ReplaceDraft(content, now)) return Fail(new("QUOTE_DRAFT_IMMUTABLE", 409, "Quote content is immutable"));
            var auditId = "quote_audit_" + Guid.NewGuid().ToString("N");
            var eventId = "quote_event_" + Guid.NewGuid().ToString("N");
            var response = new QuoteMutationResponse("quote_command_" + Guid.NewGuid().ToString("N"), command.CorrelationId,
                quote.QuoteId, "QUOTE", quote.ResourceVersion, now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), "COMMITTED",
                new(QuoteProjection.Document(quote)), [], [eventId], [auditId]);
            persistence.AddCommand(new(scope, trusted.WorkspaceId, trusted.MemberId, operation, quote.QuoteId,
                fingerprint, Serialize(response), auditId, command.RequestId, command.CorrelationId, priorVersion, quote.ResourceVersion, now));
            persistence.AddOutbox(new(eventId, trusted.WorkspaceId, quote.QuoteId, updating ? "QUOTE_DRAFT_REPLACED" : sourceDealId is null ? "QUOTE_DRAFT_CREATED" : "QUOTE_DRAFT_CREATED_FOR_DEAL",
                command.CorrelationId, Serialize(new { quoteId = quote.QuoteId, resourceVersion = quote.ResourceVersion,
                    productSnapshots = captures, workspacePolicyVersion = policy.Version, invalidatedApproval }), now));
            try { await persistence.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
            catch (QuotePersistenceConcurrencyException) { return Fail(new("VERSION_CONFLICT", 412, "Resource version conflict")); }
            return Success(response, access.Value);
        }
        finally { if (sourceLease is not null) await sourceLease.DisposeAsync(); }
    }

    internal static Dictionary<string, string> Values(QuoteReadModel model) => new()
    {
        ["buyerRef"] = Serialize(model.BuyerRef), ["sourcePath"] = Serialize(model.SourcePath), ["sourceDealId"] = Serialize(model.SourceDealId),
        ["contactId"] = Serialize(model.ContactId), ["sourceLeadId"] = Serialize(model.SourceLeadId), ["ownerId"] = Serialize(model.OwnerId),
        ["title"] = Serialize(model.Title), ["currency"] = Serialize(model.Currency), ["recipientEmail"] = Serialize(model.RecipientEmail),
        ["lineItems"] = Serialize(model.LineItems), ["adjustments"] = Serialize(model.Adjustments), ["subtotal"] = Serialize(model.Subtotal),
        ["discountTotal"] = Serialize(model.DiscountTotal), ["taxTotal"] = Serialize(model.TaxTotal), ["grandTotal"] = Serialize(model.GrandTotal),
        ["validUntil"] = Serialize(model.ValidUntil), ["notes"] = Serialize(model.Notes), ["paymentAgreement"] = Serialize(model.PaymentAgreement)
    };
    private static bool EqualMoney(QuoteMoney a, QuoteMoney b) => a.Currency == b.Currency && SameNumber(a.Amount, b.Amount);
    private static bool SameNumber(string a, string b) => decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out var left)
        && decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out var right) && left == right;
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, QuotePricing.Json);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static QuoteOperationResult<QuoteMutationResponse> Fail(QuoteOperationError error) => QuoteOperationResult<QuoteMutationResponse>.Failure(error);
    private static QuoteOperationResult<QuoteMutationResponse> Success(QuoteMutationResponse result, QuoteAccess access) =>
        QuoteOperationResult<QuoteMutationResponse>.Success(result with { Result = new(QuoteFieldSecurity.Project(result.Result.Quote, access.Authorization)) });
}
