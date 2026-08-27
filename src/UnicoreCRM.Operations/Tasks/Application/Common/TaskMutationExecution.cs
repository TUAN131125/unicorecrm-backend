using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.Common;

internal sealed class TaskMutationExecution(
    ITasksPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<TaskOperationResult<TaskMutationResponse>> ExecuteAsync(
        TaskAccess access,
        string operation,
        string eventType,
        string taskId,
        TaskCommandMetadata metadata,
        string fingerprint,
        Func<TaskItem, DateTimeOffset, bool> mutate,
        Func<TrustedWorkspaceContext, CancellationToken, Task<TaskOperationError?>>? precondition,
        Func<TaskAccess, TaskItem, Task<TaskOperationError?>> recordGuard,
        CancellationToken cancellationToken)
    {
        var trusted = access.Trusted;
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);

        // The record-access guard runs before the idempotency lookup so a replay cannot bypass it.
        // Record scope is current authorization, not a business precondition, so a caller who no
        // longer reaches a task must not be able to replay a committed command against it.
        var guarded = await persistence.ReadTaskAsync(trusted.WorkspaceId, taskId, cancellationToken);
        if (guarded is null)
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.NotFound());
        var guardError = await recordGuard(access, guarded);
        if (guardError is not null)
            return TaskOperationResult<TaskMutationResponse>.Failure(guardError);

        var scopeKey = TaskCommandSupport.ScopeKey(trusted, operation, taskId, metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = TaskCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? TaskOperationResult<TaskMutationResponse>.Success(Project(TaskCommandSupport.ReplayTask(existing), access))
                : TaskOperationResult<TaskMutationResponse>.Failure(replayError);
        }

        // Only a genuinely new command evaluates current mutable owner/member state. A committed
        // replay is answered from stored evidence alone, so a member deactivated after the original
        // commit cannot retroactively turn that command's replay into a validation failure.
        if (precondition is not null)
        {
            var preconditionError = await precondition(trusted, cancellationToken);
            if (preconditionError is not null)
                return TaskOperationResult<TaskMutationResponse>.Failure(preconditionError);
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
        return TaskOperationResult<TaskMutationResponse>.Success(Project(response, access));
    }

    /// <summary>
    /// Applies field security to the outgoing response. It is applied at the boundary because a
    /// replay returns a projection serialized under whatever policy was in force when the command
    /// committed; enforcing here makes stored evidence unable to leak a currently withheld value.
    /// </summary>
    private static TaskMutationResponse Project(TaskMutationResponse response, TaskAccess access) =>
        response with
        {
            Result = new TaskMutationResult(
                TaskFieldSecurity.Project(response.Result.Task, access.Authorization))
        };
}
