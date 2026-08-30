using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Sales.Orders.Contracts;
using UnicoreCRM.Sales.Orders.Domain;

namespace UnicoreCRM.Sales.Orders.Application.Common;

internal static class OrderProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static OrderReadModel Document(Order order) =>
        new(
            order.OrderId,
            order.OrderNumber,
            order.OrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            new OrderBuyerReference(order.BuyerType, order.BuyerId),
            order.State,
            RequiredJson<IReadOnlyList<OrderLineReadModel>>(order.LineItemsJson, "lineItems"),
            Money(order.SubtotalAmount, order.SubtotalCurrency),
            Money(order.DiscountTotalAmount, order.DiscountTotalCurrency),
            Money(order.TaxTotalAmount, order.TaxTotalCurrency),
            Money(order.GrandTotalAmount, order.GrandTotalCurrency),
            order.Currency,
            RequiredJson<OrderReadActions>(order.ActionsJson, "actions"),
            order.ResourceVersion,
            Timestamp(order.CreatedAt),
            Timestamp(order.UpdatedAt))
        {
            ContactId = order.ContactId,
            SourceLeadId = order.SourceLeadId,
            SourceQuoteId = order.SourceQuoteId,
            SourceQuoteNumber = order.SourceQuoteNumber,
            SourceDealId = order.SourceDealId,
            Adjustments = OptionalJson<IReadOnlyList<OrderCommercialAdjustmentReadModel>>(order.AdjustmentsJson),
            ConfirmedAt = Timestamp(order.ConfirmedAt),
            CompletedAt = Timestamp(order.CompletedAt),
            CancelledAt = Timestamp(order.CancelledAt),
            ExpectedDeliveryDate = order.ExpectedDeliveryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            RecipientName = order.RecipientName,
            RecipientPhone = order.RecipientPhone,
            RecipientEmail = order.RecipientEmail,
            ShippingAddress = OptionalJson<OrderShippingAddressReadModel>(order.ShippingAddressJson),
            OwnerId = order.OwnerId,
            Notes = order.Notes,
            CreditPolicyEvaluation = OptionalJson<OrderCreditPolicyEvaluationReadModel>(order.CreditPolicyEvaluationJson),
            ArchivedAt = Timestamp(order.ArchivedAt),
            ArchiveReason = order.ArchiveReason,
            CreditApproval = OptionalJson<OrderCreditApprovalSummaryReadModel>(order.CreditApprovalJson)
        };

    private static OrderMoney Money(decimal amount, string currency) =>
        new(amount.ToString("0.######", CultureInfo.InvariantCulture), currency);

    private static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string? Timestamp(DateTimeOffset? value) => value is null ? null : Timestamp(value.Value);

    private static T RequiredJson<T>(string json, string field)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Persisted Order {field} is invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Persisted Order {field} is invalid.", exception);
        }
    }

    private static T? OptionalJson<T>(string? json) => json is null ? default : RequiredJson<T>(json, typeof(T).Name);
}
