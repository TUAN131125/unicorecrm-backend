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

internal interface IContactsTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
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

    Task<IContactsTransaction> BeginSerializableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The Workspace-wide duplicate guard. It deliberately applies no record-scope predicate:
    /// uniqueness is a Workspace fact, and a scope-filtered scan would let an OWN-scoped member
    /// create exactly the duplicate this guard exists to prevent. It returns only a boolean, so no
    /// identifier, field value or cardinality of an unreadable Contact can reach the caller.
    /// </summary>
    Task<bool> AnyContactWithNormalizedEmailAsync(
        string workspaceId,
        string normalizedEmail,
        CancellationToken cancellationToken);

    Task<ContactConversionRecord?> FindConversionAsync(string scopeKey, CancellationToken cancellationToken);
    void AddContact(Contact contact);
    void AddConversion(ContactConversionRecord record);
    void AddAudit(ContactAuditRecord audit);
    void AddOutbox(ContactOutboxMessage message);
}

internal static class ContactErrors
{
    internal static ContactOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static ContactOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static ContactOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static ContactOperationError Validation(IReadOnlyDictionary<string, string[]> fields, int status = 422) =>
        new("VALIDATION_FAILED", status, "Validation failed", FieldErrors: fields);
}
