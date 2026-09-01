namespace UnicoreCRM.Billing.Payments.Domain;

internal sealed class PaymentRecord
{
    private PaymentRecord() { }

    public string WorkspaceId { get; private set; } = null!;
    public string PaymentRecordId { get; private set; } = null!;
    public string BuyerType { get; private set; } = null!;
    public string BuyerId { get; private set; } = null!;
    public string? OrderId { get; private set; }
    public string? PaymentIntentId { get; private set; }
    public string Kind { get; private set; } = null!;
    public string State { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string MethodCode { get; private set; } = null!;
    public string Channel { get; private set; } = null!;
    public string? ProviderCode { get; private set; }
    public string? RefundOfPaymentRecordId { get; private set; }
    public string? RefundOfCustomerCreditId { get; private set; }
    public string? RefundIntentId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? ExternalReference { get; private set; }
    public string? EvidenceJson { get; private set; }
    public string ReconciliationState { get; private set; } = null!;
    public string? CodCustomerCollectionState { get; private set; }
    public string? CodMerchantRemittanceState { get; private set; }
    public bool EffectiveForReceivables { get; private set; }
    public long ResourceVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string AllocationsJson { get; private set; } = null!;
    public string RefundsJson { get; private set; } = null!;
    public string CustomerCreditsJson { get; private set; } = null!;
    public decimal UnallocatedAmount { get; private set; }
    public string UnallocatedCurrency { get; private set; } = null!;
    public decimal RefundableAmount { get; private set; }
    public string RefundableCurrency { get; private set; } = null!;
}
