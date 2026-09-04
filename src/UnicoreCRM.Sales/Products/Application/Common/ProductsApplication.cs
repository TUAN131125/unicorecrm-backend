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
    /// <summary>
    /// Loads a Product for mutation. The trusted Workspace is a query predicate, not a check applied
    /// after materialisation: a Product of another Workspace must never be loaded, because deciding
    /// its Workspace afterwards is exactly what let an unknown identifier and a real foreign one
    /// produce different answers.
    /// </summary>
    Task<Product?> LoadProductAsync(string workspaceId, string productId, CancellationToken cancellationToken);

    /// <summary>Reads a Product for projection, scoped by trusted Workspace in the query itself.</summary>
    Task<Product?> ReadProductAsync(string workspaceId, string productId, CancellationToken cancellationToken);
    /// <param name="scopeOwnerMemberId">
    /// The AccessControl-resolved record-scope owner. Product has no member-owner concept, so a
    /// non-null value can never match and the query correctly returns nothing.
    /// </param>
    Task<IReadOnlyList<Product>> ReadProductsAsync(string workspaceId, string? scopeOwnerMemberId, CancellationToken cancellationToken);

    /// <summary>
    /// One batch read backing the snapshot reader. The trusted Workspace is a query predicate, so a
    /// Product of another Workspace is simply never returned and is indistinguishable from an
    /// unknown identifier.
    /// </summary>
    Task<IReadOnlyList<Product>> ReadProductSnapshotsAsync(
        string workspaceId,
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken);
    /// <summary>
    /// Loads the explicitly named Products of one batch, scoped by trusted Workspace in the query.
    /// A named Product belonging to another Workspace is simply not returned, so the batch cannot
    /// report that it exists.
    /// </summary>
    Task<IReadOnlyList<Product>> LoadProductsAsync(string workspaceId, IReadOnlyCollection<string> productIds, CancellationToken cancellationToken);
    Task<bool> SkuExistsAsync(string workspaceId, string normalizedSku, string? exceptProductId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the Workspace's Product Configuration revision anchor and its sparse overrides as one
    /// consistent snapshot, so the revision cannot come from a different state than the overrides.
    /// A Workspace with no anchor and no overrides is a valid sparse state, not an absent resource.
    /// </summary>
    Task<ProductConfigurationState> ReadProductConfigurationAsync(string workspaceId, CancellationToken cancellationToken);

    /// <summary>
    /// Loads the same configuration snapshot for a Product command, participating in the caller's
    /// already-open command transaction instead of opening its own.
    ///
    /// <para>A command must not read eligibility in a transaction that commits before the Product
    /// write: that gap is a check-then-write race in which a configuration change committed in
    /// between would let the command commit against state that no longer authorises it. Reading
    /// inside the command's serializable transaction makes the eligibility read and the Product write
    /// one linearizable unit.</para>
    /// </summary>
    Task<ProductConfigurationState> LoadProductConfigurationForCommandAsync(string workspaceId, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the same snapshot for a Product Configuration mutation, inside the caller's already-open
    /// serializable transaction and holding the rows it is about to write until that transaction ends.
    ///
    /// <para>The update lock is what makes the proven Product-command linearization deterministic
    /// rather than a race. A Product command that already read eligibility holds shared locks on this
    /// state, so the mutation's write waits for that command to commit; and once the mutation commits,
    /// a later command newly selecting the type reads the committed INACTIVE state and is rejected.
    /// Between two concurrent mutations the update lock is exclusive, so they serialize instead of
    /// deadlocking on a shared-to-exclusive upgrade.</para>
    ///
    /// <para>It deliberately does not decide anything: corruption, eligibility and the requested
    /// transition are still decided by the caller from this snapshot.</para>
    /// </summary>
    Task<ProductConfigurationState> LockProductConfigurationForMutationAsync(string workspaceId, CancellationToken cancellationToken);

    /// <summary>
    /// Stages the persistence consequence of one effective-status change, without saving: the caller
    /// saves it together with the audit record and the idempotency completion so revision,
    /// configuration, evidence and commit are one atomic unit.
    ///
    /// <para><paramref name="overrideStatus"/> carries the Model B persistence rule and no public
    /// semantics: a non-null value persists or retains that override, and null removes the override
    /// so the code falls back to the canonical default. Which of the two happened is never
    /// observable on the wire.</para>
    /// </summary>
    /// <param name="newRevision">
    /// The post-command document revision. It is only ever the prior revision plus one, and this
    /// method is only called when the effective document actually changes.
    /// </param>
    Task ApplyProductConfigurationTypeStatusAsync(
        string workspaceId,
        string productTypeCode,
        string? overrideStatus,
        long newRevision,
        CancellationToken cancellationToken);

    /// <summary>
    /// Raises the Workspace's greatest trusted configuration revision inside the caller's current
    /// transaction, using the same monotonic statement as the read path. It rolls back with the
    /// command, so a rejected or failed command establishes no trust.
    /// </summary>
    Task RaiseProductConfigurationTrustAsync(string workspaceId, long revision, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically commits the read audit record and raises the Workspace's greatest trusted
    /// configuration revision. A failure here must prevent a successful response, because success
    /// without trust evidence would leave a later rollback undetectable.
    /// </summary>
    Task RecordConfigurationReadEvidenceAsync(
        string workspaceId,
        long servedRevision,
        ProductAuditRecord audit,
        CancellationToken cancellationToken);
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

    /// <summary>
    /// Persisted Product Configuration violates a structural invariant. INTERNAL_ERROR is the
    /// admitted vocabulary for this operation; no dedicated code is invented, and the detail stays
    /// generic because the caller cannot act on it.
    /// </summary>
    internal static ProductOperationError ConfigurationCorrupt() =>
        new("INTERNAL_ERROR", 500, "Internal error");
}
