using UnicoreCRM.Operations.Tasks.Domain;
using Domain = UnicoreCRM.Operations.Tasks.Domain;

namespace UnicoreCRM.Operations.Tasks.Application.Common;

internal sealed record TaskCommandMetadata(
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    long? ExpectedVersion);

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

internal sealed record TaskListSpecification(
    int Offset,
    int Limit,
    string? Search,
    Domain.TaskStatus? Status,
    TaskPriority? Priority,
    string? AssigneeId,
    string? RelationshipType,
    string? RelationshipId,
    string? RecordModuleKey,
    string? RecordId,
    DateTimeOffset? OverdueAt,
    string SortBy,
    bool Descending);

internal sealed record ActivityListSpecification(
    int Offset,
    int Limit,
    string? Search,
    ActivityType? Type,
    string? ActorId,
    string? RelationshipType,
    string? RelationshipId,
    string? RecordModuleKey,
    string? RecordId,
    DateTimeOffset? OccurredFrom,
    DateTimeOffset? OccurredTo,
    bool Descending);

internal sealed record TasksPage<T>(IReadOnlyList<T> Items, bool HasNextPage, int? NextOffset, int TotalCount);

internal interface ITasksPersistence
{
    Task<ITasksTransaction> BeginSerializableAsync(CancellationToken cancellationToken);
    Task<TaskItem?> LoadTaskAsync(string workspaceId, string taskId, CancellationToken cancellationToken);
    Task<TaskItem?> ReadTaskAsync(string workspaceId, string taskId, CancellationToken cancellationToken);
    Task<TasksPage<TaskItem>> ListTasksAsync(string workspaceId, TaskListSpecification specification, CancellationToken cancellationToken);
    Task<TasksPage<TaskActivity>> ListActivitiesAsync(string workspaceId, ActivityListSpecification specification, CancellationToken cancellationToken);
    Task<TaskIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken);
    void AddTask(TaskItem task);
    void AddActivity(TaskActivity activity);
    void AddIdempotency(TaskIdempotencyRecord record);
    void AddAudit(TaskAuditRecord audit);
    void AddOutbox(TaskOutboxMessage message);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal interface ITasksTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

internal sealed class TasksPersistenceConcurrencyException(Exception innerException)
    : Exception("The Tasks resource changed concurrently.", innerException);

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
