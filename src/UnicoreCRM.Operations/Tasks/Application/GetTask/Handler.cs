using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;

namespace UnicoreCRM.Operations.Tasks.Application.GetTask;

internal sealed record Query(string TaskId, string RequestId, string CorrelationId);

internal sealed class Handler(
    TaskAuthorization authorization,
    ITasksPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<TaskOperationResult<TaskReadModel>> HandleAsync(Query query, CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(TaskCapabilities.Read, query.CorrelationId, cancellationToken);
        if (!access.IsSuccess)
            return TaskOperationResult<TaskReadModel>.Failure(access.Error!);
        if (!TaskValidation.IsEntityId(query.TaskId))
            return TaskOperationResult<TaskReadModel>.Failure(TaskErrors.Validation(
                new Dictionary<string, string[]> { ["taskId"] = ["taskId is not a valid entity identifier."] }));
        var trusted = access.Value!;
        var task = await persistence.ReadTaskAsync(trusted.WorkspaceId, query.TaskId, cancellationToken);
        if (task is null)
            return TaskOperationResult<TaskReadModel>.Failure(TaskErrors.NotFound());
        var now = timeProvider.GetUtcNow();
        persistence.AddAudit(new TaskAuditRecord(
            "getTask",
            trusted.WorkspaceId,
            trusted.MemberId,
            task.TaskId,
            query.RequestId,
            query.CorrelationId,
            "READ",
            task.Version,
            task.Version,
            now));
        await persistence.SaveChangesAsync(cancellationToken);
        return TaskOperationResult<TaskReadModel>.Success(TaskProjection.Task(task));
    }
}
