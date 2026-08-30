using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal static class QuoteProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static QuoteReadModel Document(Quote quote) =>
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
            RequiredJson<QuoteReadActions>(quote.ActionsJson, "actions"),
            quote.ResourceVersion,
            Timestamp(quote.CreatedAt),
            Timestamp(quote.UpdatedAt))
        {
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
            DeliveryHistory = OptionalJson<IReadOnlyList<QuoteDeliveryRecordReadModel>>(quote.DeliveryHistoryJson),
            SenderName = quote.SenderName,
            SenderAddress = quote.SenderAddress,
            SenderEmail = quote.SenderEmail,
            SenderTaxId = quote.SenderTaxId
        };

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
