using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal static class QuoteProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static QuoteReadModel Document(Quote quote, QuoteCustomerDecisionPolicy? customerPolicy = null,
        QuoteLifecyclePolicy? lifecyclePolicy = null, QuoteValidityReadModel? validity = null, bool retentionAvailable = false) =>
        new(
            quote.QuoteId,
            quote.QuoteNumber,
            quote.QuoteRevision,
            quote.RootQuoteId,
            new QuoteBuyerReference(quote.BuyerType, quote.BuyerId),
            quote.SourcePath,
            quote.Status,
            quote.Title,
            quote.Currency,
            RequiredJson<IReadOnlyList<QuoteLineReadModel>>(quote.LineItemsJson, "lineItems"),
            Money(quote.SubtotalAmount, quote.SubtotalCurrency),
            Money(quote.DiscountTotalAmount, quote.DiscountTotalCurrency),
            Money(quote.TaxTotalAmount, quote.TaxTotalCurrency),
            Money(quote.GrandTotalAmount, quote.GrandTotalCurrency),
            // State/policy hints never promote an employee report to verified acceptance.
            new QuoteReadActions(new(false, ["QUOTE_ACCEPTANCE_BLOCKED"]))
            {
                Reject = DecisionAction(quote, customerPolicy?.AllowRejection == true, customerPolicy is not null, "QUOTE_REJECTION_BLOCKED"),
                RecordCustomerResponse = DecisionAction(quote, customerPolicy?.AllowReportedAcceptance == true, customerPolicy is not null, "QUOTE_CUSTOMER_RESPONSE_BLOCKED"),
                Revise = LifecycleAction(quote, lifecyclePolicy?.AllowRevision == true && quote.Status is "SENT" or "REJECTED" or "EXPIRED", lifecyclePolicy, "QUOTE_REVISION_BLOCKED"),
                Expire = LifecycleAction(quote, lifecyclePolicy?.AllowExpiration == true && quote.Status == "SENT" && validity?.State == "OVERDUE", lifecyclePolicy, "QUOTE_EXPIRATION_BLOCKED"),
                Archive = LifecycleAction(quote, lifecyclePolicy?.AllowArchive == true && retentionAvailable && quote.Status is "DRAFT" or "REVIEW" or "REJECTED" or "EXPIRED", lifecyclePolicy, retentionAvailable ? "QUOTE_ARCHIVE_BLOCKED" : "BLOCKED_CONTRACT")
            },
            quote.ResourceVersion,
            Timestamp(quote.CreatedAt),
            Timestamp(quote.UpdatedAt))
        {
            Validity = validity,
            RevisionOfQuoteId = quote.RevisionOfQuoteId,
            SourceDealId = quote.SourceDealId,
            ContactId = quote.ContactId,
            SourceLeadId = quote.SourceLeadId,
            OwnerId = quote.OwnerId,
            RecipientEmail = quote.RecipientEmail,
            Adjustments = OptionalJson<IReadOnlyList<CommercialAdjustmentReadModel>>(quote.AdjustmentsJson),
            ValidUntil = quote.ValidUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ReviewRequestedAt = Timestamp(quote.ReviewRequestedAt),
            SentAt = Timestamp(quote.SentAt),
            AcceptedAt = Timestamp(quote.AcceptedAt),
            RejectedAt = Timestamp(quote.RejectedAt),
            ExpiredAt = Timestamp(quote.ExpiredAt),
            Notes = quote.Notes,
            ArchivedAt = Timestamp(quote.ArchivedAt),
            ArchiveReason = quote.ArchiveReason,
            ApprovalStatus = quote.ApprovalStatus,
            ApprovalRequired = quote.ApprovalRequired,
            ApprovalReasons = OptionalJson<IReadOnlyList<QuoteApprovalReasonReadModel>>(quote.ApprovalReasonsJson),
            ApprovalRequestedAt = Timestamp(quote.ApprovalRequestedAt),
            ApprovalRequestedBy = quote.ApprovalRequestedBy,
            ApprovedAt = Timestamp(quote.ApprovedAt),
            ApprovedBy = quote.ApprovedBy,
            ApprovalDecisionNote = quote.ApprovalDecisionNote,
            ApprovalContentFingerprint = quote.ApprovalContentFingerprint,
            ApprovalPolicyVersion = quote.ApprovalPolicyVersion,
            PaymentAgreement = OptionalJson<PaymentAgreementSnapshotDocument>(quote.PaymentAgreementJson),
            SalesAcceptance=quote.SalesAcceptance is null?null:QuoteSalesAcceptanceService.View(quote.SalesAcceptance),
            DeliveryHistory = OptionalJson<IReadOnlyList<QuoteDeliveryRecordReadModel>>(quote.DeliveryHistoryJson),
            CustomerDecisionHistory = quote.CustomerDecisions.Count == 0 ? null : quote.CustomerDecisions.OrderBy(x => x.ResultResourceVersion)
                .Select(x => RequiredJson<QuoteCustomerDecisionReadModel>(x.PayloadJson, "customerDecisionHistory")).ToArray(),
            SenderName = quote.SenderName,
            SenderAddress = quote.SenderAddress,
            SenderEmail = quote.SenderEmail,
            SenderTaxId = quote.SenderTaxId
        };

    private static QuoteActionAvailabilityReadModel LifecycleAction(Quote quote, bool allowed, QuoteLifecyclePolicy? policy, string blocker) =>
        new(allowed && quote.ArchivedAt is null, policy is null ? ["INTEGRATION_UNAVAILABLE"]
            : allowed && quote.ArchivedAt is null ? [] : [blocker]);

    private static QuoteActionAvailabilityReadModel DecisionAction(Quote quote, bool enabled, bool configured, string blocker) =>
        new(enabled && quote.Status == "SENT" && quote.ArchivedAt is null,
            !configured ? ["INTEGRATION_UNAVAILABLE"] : enabled && quote.Status == "SENT" && quote.ArchivedAt is null ? [] : [blocker]);

    private static QuoteMoney Money(decimal amount, string currency) =>
        new(amount.ToString("0.######", CultureInfo.InvariantCulture), currency);

    private static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string? Timestamp(DateTimeOffset? value) => value is null ? null : Timestamp(value.Value);

    private static T RequiredJson<T>(string json, string field)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Persisted Quote {field} is invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Persisted Quote {field} is invalid.", exception);
        }
    }

    private static T? OptionalJson<T>(string? json) => json is null ? default : RequiredJson<T>(json, typeof(T).Name);
}
