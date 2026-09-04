using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.CreateTask;

internal sealed record Command(CreateTaskRequest Request, TaskCommandMetadata Metadata);

internal sealed class Handler(
    TaskAuthorization authorization,
    ITasksPersistence persistence,
    IWorkspaceMemberReferenceValidator memberValidator,
    TimeProvider timeProvider)
{
    internal async Task<TaskOperationResult<TaskMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var metadata = new TaskRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(TaskCapabilities.Create, metadata, cancellationToken);
        if (!access.IsSuccess)
            return TaskOperationResult<TaskMutationResponse>.Failure(access.Error!);
        if (!CreateTaskValidation.TryCreate(command.Request, out var input, out var fields))
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.Validation(fields));

        var trusted = access.Value!.Trusted;
        var fingerprint = TaskCommandSupport.Fingerprint(input!);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = TaskCommandSupport.ScopeKey(trusted, "createTask", "WORKSPACE", command.Metadata.IdempotencyKey,
            command.Metadata.IdempotencyScopeActorId);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            // Answered from stored evidence alone, so an assignee deactivated after the original
            // commit cannot retroactively invalidate the replay or create a second Task.
            var replayError = TaskCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? TaskOperationResult<TaskMutationResponse>.Success(Project(TaskCommandSupport.ReplayTask(existing), access.Value!))
                : TaskOperationResult<TaskMutationResponse>.Failure(replayError);
        }

        // Creation is a resource-level question, so no record scope applies, but field security
        // still does: a field the caller may not write must not be written on the way in either. It
        // follows the replay branch, so a committed creation stays replayable after a field turns
        // READ_ONLY or HIDDEN - the replay writes nothing.
        var createWriteError = TaskFieldSecurity.GuardCreateWrite(access.Value!.Authorization, input!.Description, input.References);
        if (createWriteError is not null)
            return TaskOperationResult<TaskMutationResponse>.Failure(createWriteError);

        // Only a genuinely new command evaluates current mutable member state.
        if (!await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, input.AssigneeId, cancellationToken))
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.Validation(
                new Dictionary<string, string[]> { ["assigneeId"] = ["assigneeId must reference an active member of the trusted workspace."] }));

        var now = timeProvider.GetUtcNow();
        var task = new TaskItem(
            trusted.WorkspaceId,
            input.Title,
            input.Description,
            input.Priority,
            input.AssigneeId,
            input.DueAt,
            input.References,
            input.DedupeKey,
            now);
        persistence.AddTask(task);
        var response = TaskCommandSupport.RecordTaskCommit(
            persistence,
            task,
            trusted,
            command.Metadata,
            "createTask",
            "TASK_CREATED",
            scopeKey,
            "WORKSPACE",
            fingerprint,
            null,
            now);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TaskOperationResult<TaskMutationResponse>.Success(Project(response, access.Value!));
    }

    private static TaskMutationResponse Project(TaskMutationResponse response, TaskAccess access) =>
        response with
        {
            Result = new TaskMutationResult(TaskFieldSecurity.Project(response.Result.Task, access.Authorization))
        };
}
