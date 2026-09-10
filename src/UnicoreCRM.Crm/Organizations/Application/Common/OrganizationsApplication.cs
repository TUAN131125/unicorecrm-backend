using UnicoreCRM.Crm.Organizations.Domain;

namespace UnicoreCRM.Crm.Organizations.Application.Common;

internal sealed record OrganizationRequestMetadata(string RequestId, string CorrelationId);
internal sealed record OrganizationCommandMetadata(string RequestId, string CorrelationId, string IdempotencyKey, long? ExpectedVersion);

internal sealed record OrganizationOperationError(
    string Code,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

internal sealed record OrganizationOperationResult<T>(T? Value, OrganizationOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static OrganizationOperationResult<T> Success(T value) => new(value, null);
    internal static OrganizationOperationResult<T> Failure(OrganizationOperationError error) => new(default, error);
}

internal interface IOrganizationsPersistence
{
    Task<Organization?> ReadOrganizationAsync(string workspaceId, string organizationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Organization>> ReadOrganizationsAsync(string workspaceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Organization>> ReadOrganizationsAsync(string workspaceId, IReadOnlyCollection<string> organizationIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<Organization>> ListOrganizationsAsync(
        string workspaceId,
        string? scopeOwnerMemberId,
        string? ownerId,
        string? status,
        string? industry,
        string? sizeBand,
        string? normalizedSearch,
        DateTimeOffset? cursorCreatedAt,
        string? cursorOrganizationId,
        int take,
        CancellationToken cancellationToken);
    Task<Organization?> LoadOrganizationAsync(string workspaceId, string organizationId, CancellationToken cancellationToken);
    void AddOrganization(Organization organization);
    Task<OrganizationIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken);
    void AddIdempotency(OrganizationIdempotencyRecord record);
    void AddAudit(OrganizationAuditRecord record);
    void AddOutbox(OrganizationOutboxMessage message);
    Task<IOrganizationsTransaction> BeginSerializableAsync(CancellationToken cancellationToken);
    void AddReadAudit(OrganizationReadAuditRecord audit);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal interface IOrganizationsTransaction : IAsyncDisposable { Task CommitAsync(CancellationToken cancellationToken); }
internal sealed class OrganizationsPersistenceConcurrencyException : Exception { }

internal static class OrganizationErrors
{
    internal static OrganizationOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static OrganizationOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static OrganizationOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static OrganizationOperationError Validation(IReadOnlyDictionary<string, string[]> fields, int status = 422) =>
        new("VALIDATION_FAILED", status, "Validation failed", FieldErrors: fields);
    internal static OrganizationOperationError VersionConflict(string id, long expected, long current) =>
        new("RESOURCE_VERSION_CONFLICT", 409, "Resource version conflict", $"Organization {id} expected version {expected} but is version {current}.");
    internal static OrganizationOperationError IdempotencyReused() => new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused");
    internal static OrganizationOperationError AlreadyArchived() => new("ORGANIZATION_ALREADY_ARCHIVED", 409, "Organization already archived");
}
