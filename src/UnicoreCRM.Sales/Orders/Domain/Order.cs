namespace UnicoreCRM.Sales.Orders.Domain;

/// <summary>
/// Orders-owned durable read state. This slice exposes no Order creation or mutation path;
/// controlled verifier fixtures are the only current source of rows.
/// </summary>
internal sealed class Order
{
    private Order() { }

    internal string WorkspaceId { get; private set; } = null!;
    internal string OrderId { get; private set; } = null!;
    internal string OrderNumber { get; private set; } = null!;
    internal DateOnly OrderDate { get; private set; }
    internal string BuyerType { get; private set; } = null!;
    internal string BuyerId { get; private set; } = null!;
    internal string? ContactId { get; private set; }
    internal string? SourceLeadId { get; private set; }
    internal string? SourceQuoteId { get; private set; }
    internal string? SourceQuoteNumber { get; private set; }
    internal string? SourceDealId { get; private set; }
    internal string State { get; private set; } = null!;
    internal string LineItemsJson { get; private set; } = null!;
    internal string? AdjustmentsJson { get; private set; }
    internal decimal SubtotalAmount { get; private set; }
    internal string SubtotalCurrency { get; private set; } = null!;
    internal decimal DiscountTotalAmount { get; private set; }
    internal string DiscountTotalCurrency { get; private set; } = null!;
    internal decimal TaxTotalAmount { get; private set; }
    internal string TaxTotalCurrency { get; private set; } = null!;
    internal decimal GrandTotalAmount { get; private set; }
    internal string GrandTotalCurrency { get; private set; } = null!;
    internal string Currency { get; private set; } = null!;
    internal DateTimeOffset? ConfirmedAt { get; private set; }
    internal DateTimeOffset? CompletedAt { get; private set; }
    internal DateTimeOffset? CancelledAt { get; private set; }
    internal DateOnly? ExpectedDeliveryDate { get; private set; }
    internal string? RecipientName { get; private set; }
    internal string? RecipientPhone { get; private set; }
    internal string? RecipientEmail { get; private set; }
    internal string? ShippingAddressJson { get; private set; }
    internal string? OwnerId { get; private set; }
    internal string? Notes { get; private set; }
    internal string? CreditPolicyEvaluationJson { get; private set; }
    internal string ActionsJson { get; private set; } = null!;
    internal DateTimeOffset? ArchivedAt { get; private set; }
    internal string? ArchiveReason { get; private set; }
    internal long ResourceVersion { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
    internal DateTimeOffset UpdatedAt { get; private set; }
    internal string? CreditApprovalJson { get; private set; }
}
