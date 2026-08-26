using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.Common;

internal sealed record ProductRequestMetadata(string RequestId, string CorrelationId, long? ExpectedVersion = null);

internal sealed record ProductCommandMetadata(
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    long? ExpectedVersion);

internal sealed record ProductOperationError(
    string Code,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    IReadOnlyList<string>? BusinessBlockers = null,
    string? AggregateId = null,
    long? ExpectedVersion = null,
    long? CurrentVersion = null,
    string? IdempotencyKey = null);

internal sealed record ProductOperationResult<T>(T? Value, ProductOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static ProductOperationResult<T> Success(T value) => new(value, null);
    internal static ProductOperationResult<T> Failure(ProductOperationError error) => new(default, error);
}

internal interface IProductsPersistence
{
    Task<IProductsTransaction> BeginSerializableAsync(CancellationToken cancellationToken);
    Task<Product?> LoadProductAsync(string productId, CancellationToken cancellationToken);
    Task<Product?> ReadProductAsync(string productId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Product>> ReadProductsAsync(string workspaceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Product>> LoadProductsAsync(IReadOnlyCollection<string> productIds, CancellationToken cancellationToken);
    Task<bool> SkuExistsAsync(string workspaceId, string normalizedSku, string? exceptProductId, CancellationToken cancellationToken);
    Task<ProductIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken);
    void AddProduct(Product product);
    void AddIdempotency(ProductIdempotencyRecord record);
    void AddAudit(ProductAuditRecord audit);
    void AddOutbox(ProductOutboxMessage message);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal interface IProductsTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

internal sealed class ProductsPersistenceConcurrencyException(Exception innerException)
    : Exception("The Product resource changed concurrently.", innerException);

internal sealed class ProductsPersistenceUniqueException(Exception innerException)
    : Exception("A Product uniqueness constraint was violated.", innerException);

internal static class ProductErrors
{
    internal static ProductOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static ProductOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static ProductOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static ProductOperationError VersionConflict(string productId, long expected, long current) =>
        new("VERSION_CONFLICT", 412, "Resource version conflict", AggregateId: productId, ExpectedVersion: expected, CurrentVersion: current);
    internal static ProductOperationError IdempotencyReused(string key) =>
        new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused", IdempotencyKey: key);
    internal static ProductOperationError Validation(IReadOnlyDictionary<string, string[]> fields, int status = 422) =>
        new("VALIDATION_FAILED", status, "Validation failed", FieldErrors: fields);
    internal static ProductOperationError FieldValidation(IReadOnlyDictionary<string, string[]> fields) =>
        new("FIELD_VALIDATION_FAILED", 422, "Field validation failed", FieldErrors: fields);
    internal static ProductOperationError SkuConflict() =>
        new("PRODUCT_SKU_CONFLICT", 409, "Product SKU already exists");
    internal static ProductOperationError PricingInvalid(IReadOnlyDictionary<string, string[]> fields) =>
        new("PRODUCT_PRICING_INVALID", 422, "Product pricing is invalid", FieldErrors: fields);
    internal static ProductOperationError Archived(string productId) =>
        new("PRODUCT_ARCHIVED", 409, "Archived Product cannot be replaced", AggregateId: productId);
    internal static ProductOperationError ArchiveBlocked(string productId) =>
        new("PRODUCT_ARCHIVE_BLOCKED", 409, "Product cannot be archived", AggregateId: productId);
    internal static ProductOperationError RestoreBlocked(string productId) =>
        new("PRODUCT_RESTORE_BLOCKED", 409, "Product cannot be restored", AggregateId: productId);
}
