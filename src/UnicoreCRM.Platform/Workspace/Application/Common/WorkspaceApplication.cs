using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.Workspace.Application.Common;

internal sealed record WorkspaceRequest(string RequestId, string CorrelationId);

internal sealed record WorkspaceOperationError(
    string Code,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

internal sealed record WorkspaceOperationResult<T>(T? Value, WorkspaceOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static WorkspaceOperationResult<T> Success(T value) => new(value, null);
    internal static WorkspaceOperationResult<T> Failure(WorkspaceOperationError error) => new(default, error);
}

internal interface IWorkspaceContextResolver
{
    Task<TrustedWorkspaceContext?> ResolveAsync(string accountId, string memberId, string requestedWorkspaceId, CancellationToken cancellationToken);
}

internal interface ITrustedWorkspaceSetter
{
    void Set(TrustedWorkspaceContext context);
}

internal static class WorkspaceErrors
{
    internal static WorkspaceOperationError AuthenticationRequired() => new("AUTHENTICATION_REQUIRED", 401, "Authentication required");
    internal static WorkspaceOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static WorkspaceOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static WorkspaceOperationError Validation(IReadOnlyDictionary<string, string[]> fields) => new("VALIDATION_FAILED", 422, "Validation failed", null, fields);
    internal static WorkspaceOperationError ResourceNotFound(string resource) => new("RESOURCE_NOT_FOUND", 404, "Resource not found", $"{resource} was not found for the selected Workspace.");
    internal static WorkspaceOperationError VersionConflict(long expectedVersion) => new("VERSION_CONFLICT", 412, "Version conflict", $"The expected version {expectedVersion} is stale.");
    internal static WorkspaceOperationError IdempotencyKeyReused() => new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused", "The idempotency key was already used with a different request.");
    internal static WorkspaceOperationError LifecycleConflict(string detail) => new("LIFECYCLE_CONFLICT", 409, "Lifecycle conflict", detail);
}
