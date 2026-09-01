namespace UnicoreCRM.Billing.Payments.Domain;

internal sealed class PaymentIntent
{
    private PaymentIntent() { }

    public string WorkspaceId { get; private set; } = null!;
    public string PaymentIntentId { get; private set; } = null!;
    public string BuyerType { get; private set; } = null!;
    public string BuyerId { get; private set; } = null!;
    public string? OrderId { get; private set; }
    public string InvoiceIdsJson { get; private set; } = null!;
    public string ScheduleLineIdsJson { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string MethodCode { get; private set; } = null!;
    public string ProviderCode { get; private set; } = null!;
    public string State { get; private set; } = null!;
    public string? CheckoutUrl { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string? FailureCode { get; private set; }
    public string? Purpose { get; private set; }
    public long ResourceVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
