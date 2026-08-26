using System.Text.Json.Serialization;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Sales.Products.Contracts;

public static class ProductCapabilities
{
    public static AccessRequirement Read { get; } = AccessRequirement.ForCanonicalCapability("products.read");
    public static AccessRequirement Create { get; } = AccessRequirement.ForCanonicalCapability("products.create");
    public static AccessRequirement Edit { get; } = AccessRequirement.ForCanonicalCapability("products.edit");
    public static AccessRequirement Delete { get; } = AccessRequirement.ForCanonicalCapability("products.delete");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductMoney(string? Amount, string? Currency);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateProductRequest(
    string? Sku,
    string? Name,
    string? Type,
    string? Status,
    string? Category,
    string? Unit,
    ProductMoney? UnitPrice,
    string? TaxRate,
    string? TaxMode,
    string? BillingCycle,
    bool? IsSubscription,
    bool? IsRenewable,
    IReadOnlyList<string?>? Tags,
    string? Description = null,
    ProductMoney? CostPrice = null,
    int? WarrantyMonths = null,
    int? DefaultContractMonths = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceProductRequest(
    string? Sku,
    string? Name,
    string? Type,
    string? Status,
    string? Category,
    string? Unit,
    ProductMoney? UnitPrice,
    string? TaxRate,
    string? TaxMode,
    string? BillingCycle,
    bool? IsSubscription,
    bool? IsRenewable,
    IReadOnlyList<string?>? Tags,
    string? Description = null,
    ProductMoney? CostPrice = null,
    int? WarrantyMonths = null,
    int? DefaultContractMonths = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArchiveProductRequest(string? Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RestoreProductRequest(string? Reason = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductVersionItem(string? ProductId, long? ExpectedVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArchiveProductsBatchRequest(IReadOnlyList<ProductVersionItem>? Items, string? Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RestoreProductsBatchRequest(IReadOnlyList<ProductVersionItem>? Items, string? Reason = null);

public sealed record ProductDocument(
    string Id,
    string Sku,
    string Name,
    string Type,
    string Status,
    string Category,
    string Unit,
    ProductMoney UnitPrice,
    string TaxRate,
    string TaxMode,
    string BillingCycle,
    bool IsSubscription,
    bool IsRenewable,
    IReadOnlyList<string> Tags,
    long Version,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ProductMoney? CostPrice { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MarginPercent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? WarrantyMonths { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? DefaultContractMonths { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ArchivedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ArchiveReason { get; init; }
}

public sealed record ProductMutationResult(ProductDocument Product);
public sealed record ProductBatchMutationResult(IReadOnlyList<ProductDocument> Products);

public sealed record ProductMutationResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    string OccurredAt,
    string Outcome,
    ProductMutationResult Result,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,
    IReadOnlyList<string> AuditEvidenceIds);

public sealed record ProductBatchMutationResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    string OccurredAt,
    string Outcome,
    ProductBatchMutationResult Result,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,
    IReadOnlyList<string> AuditEvidenceIds);

public sealed record ProductAvailabilityReadModel(
    string ProductId,
    bool Sellable,
    string Status,
    IReadOnlyList<string> BlockerCodes,
    long ResourceVersion,
    string EvaluatedAt);

public sealed record ProductPriceProjectionReadModel(
    string ProductId,
    string Quantity,
    ProductMoney UnitPrice,
    ProductMoney Subtotal,
    ProductMoney TaxAmount,
    ProductMoney Total,
    string PricingVersion,
    string EvaluatedAt);

public sealed record ProductProblemDetails(
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
