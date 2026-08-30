using System.Text.Json.Serialization;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Sales.Quotes.Contracts;

public static class QuoteCapabilities
{
    public static AccessRequirement Read { get; } = AccessRequirement.ForCanonicalCapability("quotes.read");
}

public sealed record QuoteMoney(string Amount, string Currency);
public sealed record QuoteBuyerReference(string Type, string Id);

public sealed record QuoteLineReadModel(
    string Id,
    string ProductNameSnapshot,
    string Quantity,
    QuoteMoney UnitPrice,
    string DiscountRate,
    string TaxMode,
    QuoteMoney LineSubtotal,
    QuoteMoney LineDiscountAmount,
    QuoteMoney LineTaxAmount,
    QuoteMoney LineTotal)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ProductId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SkuSnapshot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ProductTypeSnapshot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DescriptionSnapshot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TaxRate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BillingCycleSnapshot { get; init; }
}

public sealed record CommercialAdjustmentReadModel(
    string Id,
    string Label,
    string Type,
    string Calculation,
    string Value,
    QuoteMoney Amount);

public sealed record QuoteActionAvailabilityReadModel(bool Allowed, IReadOnlyList<string> BlockerCodes);
public sealed record QuoteReadActions(QuoteActionAvailabilityReadModel Accept);

public sealed record QuoteApprovalReasonReadModel(string Code, string Label)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Actual { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Limit { get; init; }
}

public sealed record PaymentAmountRuleDocument(string Type)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public QuoteMoney? Amount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Percentage { get; init; }
}

public sealed record PaymentDueRuleDocument(string Type)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Date { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Event { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? OffsetDays { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DayBasis { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Operation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? LeadDays { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MilestoneCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FirstDueDate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Interval { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Count { get; init; }
}

public sealed record PaymentAgreementLineSnapshotDocument(
    string Id,
    int Sequence,
    string Label,
    string Purpose,
    PaymentAmountRuleDocument AmountRule,
    QuoteMoney PreviewAmount,
    PaymentDueRuleDocument DueRule,
    IReadOnlyList<string> AllowedMethodCodes,
    string FulfillmentGate)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PreferredMethodCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Channel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InvoicePolicyCode { get; init; }
}

public sealed record PaymentAgreementSnapshotDocument(
    long Version,
    string Kind,
    string Currency,
    IReadOnlyList<PaymentAgreementLineSnapshotDocument> Lines)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AcceptedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceQuoteId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PolicyVersion { get; init; }
}

public sealed record QuoteDeliveryRecordReadModel(
    string Id,
    string Channel,
    string SentAt,
    string ContentFingerprint)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EvidenceType { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RecipientEmail { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Recipient { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SentBy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FileName { get; init; }
}

public sealed record QuoteReadModel(
    string Id,
    string QuoteNumber,
    int QuoteRevision,
    string RootQuoteId,
    QuoteBuyerReference BuyerRef,
    string SourcePath,
    string Status,
    string Title,
    string Currency,
    IReadOnlyList<QuoteLineReadModel> LineItems,
    QuoteMoney Subtotal,
    QuoteMoney DiscountTotal,
    QuoteMoney TaxTotal,
    QuoteMoney GrandTotal,
    QuoteReadActions Actions,
    long ResourceVersion,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RevisionOfQuoteId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceDealId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContactId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceLeadId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OwnerId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RecipientEmail { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<CommercialAdjustmentReadModel>? Adjustments { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ValidUntil { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ReviewRequestedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SentAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AcceptedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RejectedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExpiredAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ArchivedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ArchiveReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ApprovalStatus { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ApprovalRequired { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<QuoteApprovalReasonReadModel>? ApprovalReasons { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ApprovalRequestedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ApprovalRequestedBy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ApprovedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ApprovedBy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ApprovalDecisionNote { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ApprovalContentFingerprint { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ApprovalPolicyVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PaymentAgreementSnapshotDocument? PaymentAgreement { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<QuoteDeliveryRecordReadModel>? DeliveryHistory { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SenderName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SenderAddress { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SenderEmail { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SenderTaxId { get; init; }
}

public sealed record QuoteListResponse(IReadOnlyList<QuoteReadModel> Items, QuotePageInfo PageInfo);

public sealed record QuotePageInfo(
    bool HasNextPage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalCount = null);

public sealed record QuoteProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Instance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? FieldErrors = null);
