using UnicoreCRM.Crm.Contacts.Domain;

namespace UnicoreCRM.Crm.Contacts.Application.Common;

internal sealed record ContactRequestMetadata(string RequestId, string CorrelationId);
internal sealed record ContactCommandMetadata(
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    long? ExpectedVersion);

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

internal sealed class ContactsPersistenceConcurrencyException : Exception { }
internal sealed class ContactsRelationshipConflictException : Exception { }

internal interface IContactsPersistence
{
    Task<Contact?> ReadContactAsync(string workspaceId, string contactId, CancellationToken cancellationToken);
    Task<Contact?> LoadContactAsync(string workspaceId, string contactId, CancellationToken cancellationToken);
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
    Task<ContactIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken);
    void AddIdempotency(ContactIdempotencyRecord record);
    Task<IReadOnlyList<ContactOrganizationRelationship>> ReadOrganizationRelationshipsAsync(string workspaceId, string contactId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContactCustomerRelationship>> ReadCustomerRelationshipsAsync(string workspaceId, string contactId, CancellationToken cancellationToken);
    Task<ContactOrganizationRelationship?> LoadOrganizationRelationshipAsync(string workspaceId, string contactId, string relationshipId, CancellationToken cancellationToken);
    Task<ContactCustomerRelationship?> LoadCustomerRelationshipAsync(string workspaceId, string contactId, string relationshipId, CancellationToken cancellationToken);
    Task<ContactOrganizationRelationship?> LoadActivePrimaryOrganizationRelationshipAsync(string workspaceId, string contactId, string? exceptRelationshipId, CancellationToken cancellationToken);
    Task<bool> HasActiveOrganizationRelationshipAsync(string workspaceId, string contactId, string organizationId, CancellationToken cancellationToken);
    Task<bool> HasActiveCustomerRelationshipAsync(string workspaceId, string contactId, string customerId, CancellationToken cancellationToken);
    Task<bool> HasOtherActivePrimaryCustomerRelationshipAsync(string workspaceId, string customerId, string? exceptRelationshipId, CancellationToken cancellationToken);
    void AddOrganizationRelationship(ContactOrganizationRelationship relationship);
    void AddCustomerRelationship(ContactCustomerRelationship relationship);
}

internal static class ContactErrors
{
    internal static ContactOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static ContactOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static ContactOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static ContactOperationError Validation(IReadOnlyDictionary<string, string[]> fields, int status = 422) =>
        new("VALIDATION_FAILED", status, "Validation failed", FieldErrors: fields);
    internal static ContactOperationError VersionConflict(string id, long expected, long current) =>
        new("RESOURCE_VERSION_CONFLICT", 409, "Resource version conflict", $"Contact {id} expected version {expected} but is version {current}.");
    internal static ContactOperationError IdempotencyReused() =>
        new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused");
    internal static ContactOperationError AlreadyArchived() =>
        new("CONTACT_ALREADY_ARCHIVED", 409, "Contact already archived");
    internal static ContactOperationError RelationshipConflict(string detail) =>
        new("RELATIONSHIP_CONFLICT", 409, "Relationship conflict", detail);
}
