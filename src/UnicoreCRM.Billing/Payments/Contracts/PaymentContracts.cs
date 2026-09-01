using System.Text.Json.Serialization;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Billing.Payments.Contracts;

public static class PaymentCapabilities
{
    public static AccessRequirement PlanRead { get; } = AccessRequirement.ForCanonicalCapability("payments.plan.read");
    public static AccessRequirement Read { get; } = AccessRequirement.ForCanonicalCapability("payments.read");
}

public sealed record PaymentMoney(string Amount, string Currency);
public sealed record PaymentBuyerReference(string Type, string Id);

public sealed record PaymentIntentDocument(
    string Id,
    PaymentBuyerReference BuyerRef,
    IReadOnlyList<string> InvoiceIds,
    IReadOnlyList<string> ScheduleLineIds,
    PaymentMoney Amount,
    string MethodCode,
    string ProviderCode,
    string State,
    string ExpiresAt,
    long ResourceVersion,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WorkspaceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OrderId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CheckoutUrl { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FailureCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Purpose { get; init; }
}

public sealed record PaymentIntentStatusResponse(
    string Id,
    string State,
    long ResourceVersion,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FailureCode { get; init; }
}

public sealed record PaymentEvidenceItem(
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

public sealed record PaymentSourceReference(string Type, string Id);

public sealed record PaymentAllocationDocument(
    string Id,
    PaymentBuyerReference BuyerRef,
    string InvoiceId,
    string SourceType,
    string SourceId,
    PaymentMoney Amount,
    string State,
    long ResourceVersion,
    string CreatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WorkspaceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ScheduleLineId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ReversedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ReversalReasonCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ReversalReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? AuditEvidenceIds { get; init; }
}

public sealed record PaymentRefundIntentDocument(
    string Id,
    PaymentBuyerReference BuyerRef,
    PaymentSourceReference Source,
    PaymentMoney Amount,
    string State,
    string ReasonCode,
    string Reason,
    long ResourceVersion,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WorkspaceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceReturnId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OrderId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? InvoiceIds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RefundPaymentRecordId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FailureCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ProviderCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LatestProviderAttemptId { get; init; }
}

public sealed record PaymentCustomerCreditDocument(
    string Id,
    PaymentBuyerReference BuyerRef,
    string SourcePaymentRecordId,
    PaymentMoney OriginalAmount,
    PaymentMoney AvailableAmount,
    string State,
    long ResourceVersion,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WorkspaceId { get; init; }
}

public sealed record PaymentRecordDocument(
    string Id,
    PaymentBuyerReference BuyerRef,
    string Kind,
    string State,
    PaymentMoney Amount,
    string MethodCode,
    string Channel,
    string OccurredAt,
    string ReconciliationState,
    bool EffectiveForReceivables,
    long ResourceVersion,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WorkspaceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OrderId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? IntentId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ProviderCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RefundOfPaymentRecordId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RefundOfCustomerCreditId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RefundIntentId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExternalReference { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<PaymentEvidenceItem>? Evidence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CodCustomerCollectionState { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CodMerchantRemittanceState { get; init; }
}

public sealed record PaymentRecordDetailResponse(
    PaymentRecordDocument Record,
    IReadOnlyList<PaymentAllocationDocument> Allocations,
    IReadOnlyList<PaymentRefundIntentDocument> Refunds,
    IReadOnlyList<PaymentCustomerCreditDocument> CustomerCredits,
    PaymentMoney UnallocatedAmount,
    PaymentMoney RefundableAmount);

public sealed record PaymentAmountRule(string Type)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PaymentMoney? Amount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Percentage { get; init; }
}

public sealed record PaymentDueRule(string Type)
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
    PaymentAmountRule AmountRule,
    PaymentMoney PreviewAmount,
    PaymentDueRule DueRule,
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

public sealed record PaymentPlanDocument(
    string Id,
    string OrderId,
    PaymentBuyerReference BuyerRef,
    string Kind,
    string State,
    string Currency,
    PaymentAgreementSnapshotDocument AgreementSnapshot,
    IReadOnlyList<string> ScheduleLineIds,
    int EvidenceCount,
    long ResourceVersion,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WorkspaceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SupersedesPlanId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SupersededByPlanId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ActivatedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CompletedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CancelledAt { get; init; }
}

public sealed record PaymentScheduleLineDocument(
    string Id,
    string PlanId,
    long PlanVersion,
    string OrderId,
    PaymentBuyerReference BuyerRef,
    int Sequence,
    string Label,
    string Purpose,
    PaymentAmountRule AmountRule,
    PaymentMoney Amount,
    PaymentDueRule DueRule,
    IReadOnlyList<string> AllowedMethodCodes,
    string FulfillmentGate,
    string State,
    PaymentMoney SatisfiedAmount,
    PaymentMoney OutstandingAmount,
    long ResourceVersion,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WorkspaceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ResolvedDueDate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PreferredMethodCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Channel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InvoicePolicyCode { get; init; }
}

public sealed record PaymentProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? FieldErrors = null);
