using System.Text.Json.Serialization;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Sales.Orders.Contracts;

public static class OrderCapabilities
{
    public static AccessRequirement Read { get; } = AccessRequirement.ForCanonicalCapability("orders.read");
}

public sealed record OrderMoney(string Amount, string Currency);
public sealed record OrderBuyerReference(string Type, string Id);

public sealed record OrderLineReadModel(
    string Id,
    string ProductId,
    string ProductNameSnapshot,
    string Quantity,
    OrderMoney UnitPrice,
    string DiscountRate,
    string TaxMode,
    OrderMoney LineSubtotal,
    OrderMoney LineDiscountAmount,
    OrderMoney LineTaxAmount,
    OrderMoney LineTotal)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SkuSnapshot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ProductTypeSnapshot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DescriptionSnapshot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TaxRate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BillingCycleSnapshot { get; init; }
}

public sealed record OrderCommercialAdjustmentReadModel(
    string Id,
    string Label,
    string Type,
    string Calculation,
    string Value,
    OrderMoney Amount);

public sealed record OrderShippingAddressReadModel(string Line1, string City)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Line2 { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Ward { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? District { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Country { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PostalCode { get; init; }
}

public sealed record OrderCreditPolicyEvaluationReadModel(string Status, IReadOnlyList<string> BlockerCodes)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PolicyVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EvaluatedAt { get; init; }
}

public sealed record OrderCreditApprovalSummaryReadModel(
    string Id,
    string State,
    OrderMoney Amount,
    string PolicyVersion,
    long OrderResourceVersion,
    long PaymentPlanResourceVersion,
    long ResourceVersion);

public sealed record OrderActionAvailabilityReadModel(bool Allowed, IReadOnlyList<string> BlockerCodes);
public sealed record OrderReadActions(OrderActionAvailabilityReadModel Confirm, OrderActionAvailabilityReadModel Cancel);

public sealed record OrderReadModel(
    string Id,
    string OrderNumber,
    string OrderDate,
    OrderBuyerReference BuyerRef,
    string State,
    IReadOnlyList<OrderLineReadModel> LineItems,
    OrderMoney Subtotal,
    OrderMoney DiscountTotal,
    OrderMoney TaxTotal,
    OrderMoney GrandTotal,
    string Currency,
    OrderReadActions Actions,
    long ResourceVersion,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContactId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceLeadId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceQuoteId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceQuoteNumber { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceDealId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<OrderCommercialAdjustmentReadModel>? Adjustments { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ConfirmedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CompletedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CancelledAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExpectedDeliveryDate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RecipientName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RecipientPhone { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RecipientEmail { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OrderShippingAddressReadModel? ShippingAddress { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OwnerId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OrderCreditPolicyEvaluationReadModel? CreditPolicyEvaluation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ArchivedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ArchiveReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OrderCreditApprovalSummaryReadModel? CreditApproval { get; init; }
}

public sealed record OrderListResponse(IReadOnlyList<OrderReadModel> Items, OrderPageInfo PageInfo);

public sealed record OrderPageInfo(
    bool HasNextPage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalCount = null);

public sealed record OrderProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Instance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? FieldErrors = null);
