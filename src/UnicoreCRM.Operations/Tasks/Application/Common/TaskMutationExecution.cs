using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.Common;

internal sealed class TaskMutationExecution(
    ITasksPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<TaskOperationResult<TaskMutationResponse>> ExecuteAsync(
        TrustedWorkspaceContext trusted,
        string operation,
        string eventType,
        string taskId,
        TaskCommandMetadata metadata,
        string fingerprint,
        Func<TaskItem, DateTimeOffset, bool> mutate,
        Func<TrustedWorkspaceContext, CancellationToken, Task<TaskOperationError?>>? precondition,
        CancellationToken cancellationToken)
    {
        if (precondition is not null)
        {
            var error = await precondition(trusted, cancellationToken);
            if (error is not null)
                return TaskOperationResult<TaskMutationResponse>.Failure(error);
        }

        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = TaskCommandSupport.ScopeKey(trusted, operation, taskId, metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = TaskCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? TaskOperationResult<TaskMutationResponse>.Success(TaskCommandSupport.ReplayTask(existing))
                : TaskOperationResult<TaskMutationResponse>.Failure(replayError);
        }

        var task = await persistence.LoadTaskAsync(trusted.WorkspaceId, taskId, cancellationToken);
        if (task is null)
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.NotFound());
        var expectedVersion = metadata.ExpectedVersion!.Value;
        if (task.Version != expectedVersion)
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.VersionConflict(task.TaskId, expectedVersion, task.Version));
        var priorVersion = task.Version;
        var now = timeProvider.GetUtcNow();
        if (!mutate(task, now))
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.InvalidTransition(task.TaskId));
        var response = TaskCommandSupport.RecordTaskCommit(
            persistence,
            task,
            trusted,
            metadata,
            operation,
            eventType,
            scopeKey,
            taskId,
            fingerprint,
            priorVersion,
            now);
        try
        {
            await persistence.SaveChangesAsync(cancellationToken);
        }
        catch (TasksPersistenceConcurrencyException)
        {
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.VersionConflict(task.TaskId, expectedVersion, task.Version));
        }
        await transaction.CommitAsync(cancellationToken);
        return TaskOperationResult<TaskMutationResponse>.Success(response);
    }
}
