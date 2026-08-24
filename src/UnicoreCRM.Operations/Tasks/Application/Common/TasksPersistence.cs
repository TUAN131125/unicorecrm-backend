using UnicoreCRM.Operations.Tasks.Domain;

namespace UnicoreCRM.Operations.Tasks.Application.Common;

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
