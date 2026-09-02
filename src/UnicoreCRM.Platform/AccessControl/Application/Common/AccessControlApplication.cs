using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.Common;

internal sealed record AccessOperationError(
    string Code,
    int Status,
    string Title,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    string? IdempotencyKey = null);

internal sealed record AccessOperationResult<T>(T? Value, AccessOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static AccessOperationResult<T> Success(T value) => new(value, null);
    internal static AccessOperationResult<T> Failure(AccessOperationError error) => new(default, error);
}

internal interface IAccessControlPersistence
{
    Task<EffectiveAccessState> LoadEffectiveStateAsync(string workspaceId, string membershipId, CancellationToken cancellationToken);
    void AddDecision(AuthorizationDecisionRecord decision);
    void AddRecordDecision(RecordAccessDecisionRecord decision);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The result of the one authoritative capability evaluation a request performs. The context is the
/// effective policy projection produced by that same evaluation, so no consumer has to load policy a
/// second time to learn who the caller is. It is present whenever the trusted Workspace resolved,
/// including on a denial.
/// </summary>
internal sealed record AccessContextAuthorization(
    bool IsAllowed,
    string Code,
    AuthorizationContextDocument? Context);

/// <summary>
/// Evaluates one capability and returns the effective context from that same evaluation. This is the
/// only admitted way to obtain an authorization context alongside a business-capability decision:
/// authorizing a context capability first and then testing the business capability against the
/// returned capability set would audit the wrong capability and load policy twice.
/// </summary>
internal interface IAccessContextAuthorizer
{
    Task<AccessContextAuthorization> AuthorizeWithContextAsync(
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken);
}

internal interface IResolvedAuthorizationContextSetter
{
    void Set(AuthorizationContextDocument context);
}

internal static class AccessErrors
{
    internal static AccessOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static AccessOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static AccessOperationError Validation(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        new("VALIDATION_FAILED", 422, "Validation failed", fieldErrors);
    internal static AccessOperationError RoleNameConflict() => new("ROLE_NAME_CONFLICT", 409, "Role name conflict");
    internal static AccessOperationError IdempotencyKeyReused(string key) =>
        new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused", IdempotencyKey: key);
    internal static AccessOperationError LifecycleConflict() => new("LIFECYCLE_CONFLICT", 409, "Lifecycle conflict");
    internal static AccessOperationError ResourceNotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static AccessOperationError VersionConflict() => new("VERSION_CONFLICT", 412, "Version conflict");
    internal static AccessOperationError RoleInactive() => new("ROLE_INACTIVE", 409, "Role inactive");
    internal static AccessOperationError LastWorkspaceAdministrator() =>
        new("LAST_WORKSPACE_ADMINISTRATOR", 409, "Last Workspace administrator");
    internal static AccessOperationError IntegrationUnavailable() => new("INTEGRATION_UNAVAILABLE", 503, "Integration unavailable");
}
