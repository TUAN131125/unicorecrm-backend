using System.Text.Json.Serialization;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Billing.Invoices.Contracts;

public static class InvoiceCapabilities
{
    public static AccessRequirement Read { get; } = AccessRequirement.ForCanonicalCapability("invoices.read");
}

public sealed record InvoiceMoney(string Amount, string Currency);

public sealed record InvoiceBuyerReference(string Type, string Id);

public sealed record InvoiceLegalPartySnapshot(
    string DisplayName,
    IReadOnlyList<string> AddressLines)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LegalName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TaxId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Email { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Phone { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CountryCode { get; init; }
}

public sealed record InvoiceExchangeRateSnapshot(
    string FromCurrency,
    string ToCurrency,
    string Rate,
    string EffectiveAt,
    string Source,
    string RateId,
    long RateVersion);

public sealed record InvoiceLineDocument(
    string Id,
    string Description,
    string Quantity,
    InvoiceMoney UnitPrice,
    InvoiceMoney DiscountAmount,
    InvoiceMoney TaxAmount,
    InvoiceMoney LineTotal)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceOrderLineId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OrderLineId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ProductId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SkuSnapshot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? UnitOfMeasure { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceOrderQuantity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AlreadyInvoicedQuantity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InvoiceableQuantity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DiscountRate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TaxRate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; init; }
}

public sealed record InvoiceTotals(
    InvoiceMoney Subtotal,
    InvoiceMoney DiscountTotal,
    InvoiceMoney TaxTotal,
    InvoiceMoney GrandTotal)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public InvoiceMoney? RoundingAdjustment { get; init; }
}

public sealed record InvoiceSourceLinks
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OrderId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? PaymentScheduleLineIds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? ShippingBookingIds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? ReturnIds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? MilestoneCodes { get; init; }
}

public sealed record InvoiceEvidenceItem(
    string Id,
    string Type,
    string CapturedAt,
    string CapturedBy,
    string VerificationState,
    string CreatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FileName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MimeType { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Url { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExternalReference { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? LockedByBusinessEvent { get; init; }
}

/// <summary>
/// The adopted <c>InvoiceDocument</c> contract returned by both <c>getInvoice</c> and
/// <c>listInvoices</c>. Constructor parameters are the contract-required fields; every init-only
/// property is contract-optional and is omitted from the wire when absent.
/// </summary>
public sealed record InvoiceDocument(
    string Id,
    InvoiceBuyerReference BuyerRef,
    InvoiceLegalPartySnapshot SellerSnapshot,
    InvoiceLegalPartySnapshot BuyerSnapshot,
    string LifecycleState,
    string DeliveryState,
    string Currency,
    IReadOnlyList<InvoiceLineDocument> Lines,
    InvoiceTotals Totals,
    InvoiceSourceLinks SourceLinks,
    long Version,
    string IdempotencyKey,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WorkspaceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InvoiceNumber { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? IssueDate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DueDate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public InvoiceExchangeRateSnapshot? ExchangeRateSnapshot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PaymentTerms { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CreationIntentId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? IssuedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? IssueFailureCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<InvoiceEvidenceItem>? IssueEvidence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DiscardedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? VoidedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? VoidReason { get; init; }
}

public sealed record InvoiceProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? FieldErrors = null);
