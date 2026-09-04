
namespace UnicoreCRM.Operations.Tasks.Application.Common;

internal sealed record TaskRequestMetadata(string RequestId, string CorrelationId);

internal sealed record TaskCommandMetadata(
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    long? ExpectedVersion,
    string? IdempotencyScopeActorId = null);

internal sealed record TaskOperationError(
    string Code,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    string? AggregateId = null,
    long? ExpectedVersion = null,
    long? CurrentVersion = null,
    string? IdempotencyKey = null);

internal sealed record TaskOperationResult<T>(T? Value, TaskOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static TaskOperationResult<T> Success(T value) => new(value, null);
    internal static TaskOperationResult<T> Failure(TaskOperationError error) => new(default, error);
}

internal static class TaskErrors
{
    internal static TaskOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static TaskOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static TaskOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static TaskOperationError InvalidTransition(string taskId) =>
        new("TASK_INVALID_TRANSITION", 409, "Task transition is not allowed", AggregateId: taskId);
    internal static TaskOperationError VersionConflict(string taskId, long expected, long current) =>
        new("VERSION_CONFLICT", 412, "Resource version conflict", AggregateId: taskId, ExpectedVersion: expected, CurrentVersion: current);
    internal static TaskOperationError IdempotencyReused(string key) =>
        new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused", IdempotencyKey: key);
    internal static TaskOperationError Validation(IReadOnlyDictionary<string, string[]> fields) =>
        new("VALIDATION_FAILED", 422, "Validation failed", FieldErrors: fields);
}
