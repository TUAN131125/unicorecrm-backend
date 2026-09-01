namespace UnicoreCRM.Billing.Invoices.Domain;

/// <summary>
/// The Invoices-owned durable read state behind the admitted <c>InvoiceDocument</c> contract.
///
/// <para>Every member traces to exactly one property of that contract. Foreign identifiers
/// (buyer, order, schedule lines, bookings, returns) are carried as scalar snapshot values inside
/// Invoices-owned state; no foreign key, no foreign table and no foreign owner lookup exists.</para>
///
/// <para>The composite document fields are persisted as the admitted wire documents themselves, so
/// a read emits the historical snapshot exactly as recorded and never recomputes invoice money.</para>
/// </summary>
internal sealed class Invoice
{
    private Invoice() { }

    public string WorkspaceId { get; private set; } = null!;
    public string InvoiceId { get; private set; } = null!;
    public string? InvoiceNumber { get; private set; }
    public string BuyerType { get; private set; } = null!;
    public string BuyerId { get; private set; } = null!;
    public string SellerSnapshotJson { get; private set; } = null!;
    public string BuyerSnapshotJson { get; private set; } = null!;
    public string LifecycleState { get; private set; } = null!;
    public string DeliveryState { get; private set; } = null!;
    public DateOnly? IssueDate { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public string Currency { get; private set; } = null!;
    public string? ExchangeRateSnapshotJson { get; private set; }
    public string? PaymentTerms { get; private set; }
    public string? CreationIntentId { get; private set; }
    public string LinesJson { get; private set; } = null!;
    public string TotalsJson { get; private set; } = null!;
    public string SourceLinksJson { get; private set; } = null!;
    public long ResourceVersion { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? IssuedAt { get; private set; }
    public string? IssueFailureCode { get; private set; }
    public string? IssueEvidenceJson { get; private set; }
    public DateTimeOffset? DiscardedAt { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string? VoidReason { get; private set; }
}
