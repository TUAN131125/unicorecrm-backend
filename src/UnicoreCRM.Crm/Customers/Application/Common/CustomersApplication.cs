using UnicoreCRM.Crm.Customers.Domain;

namespace UnicoreCRM.Crm.Customers.Application.Common;

internal sealed record CustomerRequestMetadata(string RequestId, string CorrelationId);
internal sealed record CustomerCommandMetadata(string RequestId, string CorrelationId, string IdempotencyKey, long? ExpectedVersion);

internal sealed record CustomerOperationError(
    string Code,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

internal sealed record CustomerOperationResult<T>(T? Value, CustomerOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static CustomerOperationResult<T> Success(T value) => new(value, null);
    internal static CustomerOperationResult<T> Failure(CustomerOperationError error) => new(default, error);
}

internal interface ICustomersPersistence
{
    Task<Customer?> ReadCustomerAsync(string workspaceId, string customerId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Customer>> ReadCustomersAsync(string workspaceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Customer>> ReadCustomersAsync(string workspaceId, IReadOnlyCollection<string> customerIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<Customer>> ListCustomersAsync(string workspaceId, string? scopeOwnerMemberId, string? ownerId,
        string? type, string? status, string? segment, string? tier, string? normalizedSearch,
        DateTimeOffset? cursorCreatedAt, string? cursorCustomerId, int take, CancellationToken cancellationToken);
    Task<Customer?> LoadCustomerAsync(string workspaceId, string customerId, CancellationToken cancellationToken);
    Task<bool> SubjectExistsAsync(string workspaceId, string relationshipType, string relationshipId, CancellationToken cancellationToken);
    void AddCustomer(Customer customer);
    Task<CustomerIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken);
    void AddIdempotency(CustomerIdempotencyRecord record);
    void AddAudit(CustomerAuditRecord record);
    void AddOutbox(CustomerOutboxMessage message);
    Task<ICustomersTransaction> BeginSerializableAsync(CancellationToken cancellationToken);
    void AddReadAudit(CustomerReadAuditRecord audit);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal interface ICustomersTransaction : IAsyncDisposable { Task CommitAsync(CancellationToken cancellationToken); }
internal sealed class CustomersPersistenceConcurrencyException : Exception { }
internal sealed class CustomerBusinessKeyConflictException : Exception { }

internal static class CustomerErrors
{
    internal static CustomerOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static CustomerOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static CustomerOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static CustomerOperationError Validation(IReadOnlyDictionary<string, string[]> fields, int status = 422) =>
        new("VALIDATION_FAILED", status, "Validation failed", FieldErrors: fields);
    internal static CustomerOperationError VersionConflict(string id, long expected, long current) =>
        new("RESOURCE_VERSION_CONFLICT", 412, "Resource version conflict", $"Customer {id} expected version {expected} but is version {current}.");
    internal static CustomerOperationError IdempotencyReused() => new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused");
    internal static CustomerOperationError DuplicateSubject() => new("DUPLICATE_BUSINESS_KEY", 409, "Duplicate business key", "A Customer already exists for this account subject.");
    internal static CustomerOperationError LifecycleConflict() => new("LIFECYCLE_CONFLICT", 409, "Lifecycle conflict");
}
