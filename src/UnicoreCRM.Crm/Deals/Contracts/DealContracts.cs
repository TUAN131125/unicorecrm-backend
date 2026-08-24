using System.Text.Json.Serialization;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Deals.Contracts;

public static class DealCapabilities
{
    public static AccessRequirement Read { get; } = AccessRequirement.ForCanonicalCapability("deals.read");
    public static AccessRequirement Create { get; } = AccessRequirement.ForCanonicalCapability("deals.create");
    public static AccessRequirement Update { get; } = AccessRequirement.ForCanonicalCapability("deals.update");
    public static AccessRequirement Assign { get; } = AccessRequirement.ForCanonicalCapability("deals.assign");
    public static AccessRequirement Close { get; } = AccessRequirement.ForCanonicalCapability("deals.close");
    public static AccessRequirement Delete { get; } = AccessRequirement.ForCanonicalCapability("deals.delete");
    public static AccessRequirement Bulk { get; } = AccessRequirement.ForCanonicalCapability("deals.bulk");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DealMoney(string? Amount, string? Currency);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DealBuyerReference(string? Type, string? Id);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DealLineInput(
    string? ProductId,
    string? Quantity,
    DealMoney? UnitPrice,
    string? DiscountRate,
    string? TaxMode,
    string? TaxRate = null,
    string? BillingCycleSnapshot = null,
    string? DescriptionSnapshot = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateDealRequest(
    string? Name,
    DealBuyerReference? BuyerRef,
    string? StageCode,
    DealMoney? Amount,
    string? OpportunityScore,
    string? OwnerId,
    string? ExpectedCloseDate,
    IReadOnlyList<string>? InterestedProductIds,
    IReadOnlyList<DealLineInput>? LineItems,
    string? ContactId = null,
    string? SourceLeadId = null,
    string? ForecastCategory = null,
    string? NextActionAt = null,
    string? NextActionSummary = null,
    string? NextActionTaskId = null,
    string? Notes = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceDealProfileRequest(
    string? Name,
    DealBuyerReference? BuyerRef,
    DealMoney? Amount,
    IReadOnlyList<string>? InterestedProductIds,
    IReadOnlyList<DealLineInput>? LineItems,
    string? ContactId = null,
    string? SourceLeadId = null,
    string? Notes = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangeDealStageRequest(string? StageCode);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssignDealOwnerRequest(string? OwnerId, string? Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateDealForecastRequest(
    string? ExpectedCloseDate = null,
    string? OpportunityScore = null,
    string? ForecastCategory = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateDealNextActionRequest(
    string? NextActionAt,
    string? NextActionSummary = null,
    string? TaskId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DealWinEvidence(string? Type, string? SourceId, string? OccurredAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MarkDealWonRequest(DealWinEvidence? Evidence);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record MarkDealLostRequest(
    string? Reason,
    string? RecycleDecision,
    string? Note = null,
    string? RevisitAt = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArchiveDealRequest(string? Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArchiveDealBatchItem(string? DealId, long? ExpectedVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArchiveDealsBatchRequest(IReadOnlyList<ArchiveDealBatchItem>? Items, string? Reason);

public sealed record DealReadModel(
    string Id,
    string Name,
    DealBuyerReference BuyerRef,
    string StageCode,
    string StageCategory,
    DealMoney Amount,
    string OpportunityScore,
    string OwnerId,
    string ExpectedCloseDate,
    IReadOnlyList<string> InterestedProductIds,
    IReadOnlyList<DealLineReadModel> LineItems,
    long ResourceVersion,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContactId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceLeadId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WonAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LostAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ActualCloseDate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LostReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ArchivedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ArchiveReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ForecastCategory { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<DealForecastHistoryReadModel>? ForecastHistory { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? StageEnteredAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? NextActionAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? NextActionSummary { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DealNextActionReference? NextActionRef { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DealWinEvidence? WinEvidence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LostReasonNote { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RecycleDecision { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? RecycleEligible { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RevisitAt { get; init; }
}

public sealed record DealLineReadModel(
    string Id,
    string ProductId,
    string ProductNameSnapshot,
    string Quantity,
    DealMoney UnitPrice,
    string DiscountRate,
    string TaxMode,
    DealMoney LineSubtotal,
    DealMoney LineDiscountAmount,
    DealMoney LineTaxAmount,
    DealMoney LineTotal)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SkuSnapshot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ProductTypeSnapshot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DescriptionSnapshot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TaxRate { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BillingCycleSnapshot { get; init; }
}

public sealed record DealForecastHistoryReadModel(
    string Id,
    string OccurredAt,
    string PreviousExpectedCloseDate,
    string NextExpectedCloseDate,
    string PreviousProbability,
    string NextProbability,
    string PreviousCategory,
    string NextCategory,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Actor = null);

public sealed record DealNextActionReference(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Id = null);

public sealed record DealListResponse(IReadOnlyList<DealReadModel> Items, DealPageInfo PageInfo);

public sealed record DealPageInfo(
    bool HasNextPage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalCount = null);

public sealed record DealForecastSummaryReadModel(
    string AsOf,
    IReadOnlyList<DealForecastCurrencyBucket> Buckets,
    bool PermissionFiltered);

public sealed record DealForecastCurrencyBucket(
    string Currency,
    int OpenDealCount,
    int OverdueDealCount,
    int ClosingThisMonthCount,
    DealMoney OpenAmount,
    DealMoney CommitAmount,
    DealMoney BestCaseAmount,
    DealMoney PipelineAmount,
    DealMoney WeightedAmount);

public sealed record DealMutationResult(DealReadModel Deal);
public sealed record DealBatchMutationResult(IReadOnlyList<DealReadModel> Deals);

public sealed record DealMutationResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    string OccurredAt,
    string Outcome,
    DealMutationResult Result,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,
    IReadOnlyList<string> AuditEvidenceIds);

public sealed record DealBatchMutationResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    string OccurredAt,
    string Outcome,
    DealBatchMutationResult Result,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,
    IReadOnlyList<string> AuditEvidenceIds);

public sealed record DealProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Instance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? BusinessBlockers = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AggregateId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ExpectedVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? CurrentVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IdempotencyKey = null);
