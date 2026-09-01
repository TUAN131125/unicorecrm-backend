namespace UnicoreCRM.Billing.Payments.Domain;

internal sealed class PaymentPlan
{
    private PaymentPlan() { }

    public string WorkspaceId { get; private set; } = null!;
    public string PaymentPlanId { get; private set; } = null!;
    public string OrderId { get; private set; } = null!;
    public string BuyerType { get; private set; } = null!;
    public string BuyerId { get; private set; } = null!;
    public string Kind { get; private set; } = null!;
    public string State { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public string AgreementSnapshotJson { get; private set; } = null!;
    public string ScheduleLineIdsJson { get; private set; } = null!;
    public string? SupersedesPlanId { get; private set; }
    public string? SupersededByPlanId { get; private set; }
    public int EvidenceCount { get; private set; }
    public long ResourceVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
}

internal sealed class PaymentScheduleLine
{
    private PaymentScheduleLine() { }

    public string WorkspaceId { get; private set; } = null!;
    public string PaymentScheduleLineId { get; private set; } = null!;
    public string PaymentPlanId { get; private set; } = null!;
    public long PaymentPlanVersion { get; private set; }
    public string OrderId { get; private set; } = null!;
    public string BuyerType { get; private set; } = null!;
    public string BuyerId { get; private set; } = null!;
    public int Sequence { get; private set; }
    public string Label { get; private set; } = null!;
    public string Purpose { get; private set; } = null!;
    public string AmountRuleJson { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string AmountCurrency { get; private set; } = null!;
    public string DueRuleJson { get; private set; } = null!;
    public DateOnly? ResolvedDueDate { get; private set; }
    public string AllowedMethodCodesJson { get; private set; } = null!;
    public string? PreferredMethodCode { get; private set; }
    public string? Channel { get; private set; }
    public string FulfillmentGate { get; private set; } = null!;
    public string? InvoicePolicyCode { get; private set; }
    public string State { get; private set; } = null!;
    public decimal SatisfiedAmount { get; private set; }
    public string SatisfiedCurrency { get; private set; } = null!;
    public decimal OutstandingAmount { get; private set; }
    public string OutstandingCurrency { get; private set; } = null!;
    public long ResourceVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
