namespace UnicoreCRM.Operations.Tasks.Contracts;

public enum TaskSummaryReadStatus
{
    Succeeded,
    AccessDenied,
    WorkspaceMismatch,
    InvalidReference,
    NotFound
}

public sealed record TaskSummaryProjection(
    string TaskId,
    string? Title,
    string? Status,
    string? Priority,
    string? DueAt);

public sealed record TaskSummaryReadResult(
    TaskSummaryReadStatus Status,
    TaskSummaryProjection? Summary = null);

public interface ITaskSummaryReader
{
    Task<TaskSummaryReadResult> ReadAsync(
        string taskId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken);
}
