using UnicoreCRM.Crm.Customers.Domain;

namespace UnicoreCRM.Crm.Customers.Application.Common;

internal sealed record CustomerRequestMetadata(string RequestId, string CorrelationId);

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
    void AddReadAudit(CustomerReadAuditRecord audit);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal static class CustomerErrors
{
    internal static CustomerOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static CustomerOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static CustomerOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static CustomerOperationError Validation(IReadOnlyDictionary<string, string[]> fields, int status = 422) =>
        new("VALIDATION_FAILED", status, "Validation failed", FieldErrors: fields);
}
