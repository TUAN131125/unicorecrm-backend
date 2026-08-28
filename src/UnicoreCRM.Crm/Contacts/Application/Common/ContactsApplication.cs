using UnicoreCRM.Crm.Contacts.Domain;

namespace UnicoreCRM.Crm.Contacts.Application.Common;

internal sealed record ContactRequestMetadata(string RequestId, string CorrelationId);

internal sealed record ContactOperationError(
    string Code,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

internal sealed record ContactOperationResult<T>(T? Value, ContactOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static ContactOperationResult<T> Success(T value) => new(value, null);
    internal static ContactOperationResult<T> Failure(ContactOperationError error) => new(default, error);
}

internal interface IContactsPersistence
{
    Task<Contact?> ReadContactAsync(string workspaceId, string contactId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Contact>> ReadContactsAsync(
        string workspaceId,
        string? scopeOwnerMemberId,
        CancellationToken cancellationToken);
    void AddReadAudit(ContactReadAuditRecord audit);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal static class ContactErrors
{
    internal static ContactOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static ContactOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static ContactOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static ContactOperationError Validation(IReadOnlyDictionary<string, string[]> fields, int status = 422) =>
        new("VALIDATION_FAILED", status, "Validation failed", FieldErrors: fields);
}
