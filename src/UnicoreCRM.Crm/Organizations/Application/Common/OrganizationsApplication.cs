using UnicoreCRM.Crm.Organizations.Domain;

namespace UnicoreCRM.Crm.Organizations.Application.Common;

internal sealed record OrganizationRequestMetadata(string RequestId, string CorrelationId);

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
    void AddReadAudit(OrganizationReadAuditRecord audit);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal static class OrganizationErrors
{
    internal static OrganizationOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static OrganizationOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static OrganizationOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static OrganizationOperationError Validation(IReadOnlyDictionary<string, string[]> fields, int status = 422) =>
        new("VALIDATION_FAILED", status, "Validation failed", FieldErrors: fields);
}
