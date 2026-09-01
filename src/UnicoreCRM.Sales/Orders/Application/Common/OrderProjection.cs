using System.Globalization;
using UnicoreCRM.Sales.Orders.Contracts;
using UnicoreCRM.Sales.Orders.Domain;

namespace UnicoreCRM.Sales.Orders.Application.Common;

internal static class OrderProjection
{
    internal static OrderReadModel Document(Order order) =>
        new(
            order.OrderId,
            order.OrderNumber,
            order.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            new OrderBuyerReference(order.BuyerType, order.BuyerId),
            order.State,
            OrderPersistedDocuments.LineItems(order.LineItemsJson),
            Money(order.SubtotalAmount, order.SubtotalCurrency),
            Money(order.DiscountTotalAmount, order.DiscountTotalCurrency),
            Money(order.TaxTotalAmount, order.TaxTotalCurrency),
            Money(order.GrandTotalAmount, order.GrandTotalCurrency),
            order.Currency,
            OrderPersistedDocuments.Actions(order.ActionsJson),
            order.ResourceVersion,
            Timestamp(order.CreatedAt),
            Timestamp(order.UpdatedAt))
        {
            ContactId = order.ContactId,
            SourceLeadId = order.SourceLeadId,
            SourceQuoteId = order.SourceQuoteId,
            SourceQuoteNumber = order.SourceQuoteNumber,
            SourceDealId = order.SourceDealId,
            Adjustments = OrderPersistedDocuments.Adjustments(order.AdjustmentsJson),
            ConfirmedAt = Timestamp(order.ConfirmedAt),
            CompletedAt = Timestamp(order.CompletedAt),
            CancelledAt = Timestamp(order.CancelledAt),
            ExpectedDeliveryDate = order.ExpectedDeliveryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            RecipientName = order.RecipientName,
            RecipientPhone = order.RecipientPhone,
            RecipientEmail = order.RecipientEmail,
            ShippingAddress = OrderPersistedDocuments.ShippingAddress(order.ShippingAddressJson),
            OwnerId = order.OwnerId,
            Notes = order.Notes,
            CreditPolicyEvaluation = OrderPersistedDocuments.CreditPolicyEvaluation(order.CreditPolicyEvaluationJson),
            ArchivedAt = Timestamp(order.ArchivedAt),
            ArchiveReason = order.ArchiveReason,
            CreditApproval = OrderPersistedDocuments.CreditApproval(order.CreditApprovalJson)
        };

    private static OrderMoney Money(decimal amount, string currency) =>
        new(amount.ToString("0.######", CultureInfo.InvariantCulture), currency);

    private static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string? Timestamp(DateTimeOffset? value) => value is null ? null : Timestamp(value.Value);

}
