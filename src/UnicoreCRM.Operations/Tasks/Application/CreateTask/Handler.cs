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
        if (!await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, input!.AssigneeId, cancellationToken))
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.Validation(
                new Dictionary<string, string[]> { ["assigneeId"] = ["assigneeId must reference an active member of the trusted workspace."] }));

        var fingerprint = TaskCommandSupport.Fingerprint(input);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = TaskCommandSupport.ScopeKey(trusted, "createTask", "WORKSPACE", command.Metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = TaskCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? TaskOperationResult<TaskMutationResponse>.Success(TaskCommandSupport.ReplayTask(existing))
                : TaskOperationResult<TaskMutationResponse>.Failure(replayError);
        }

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
        return TaskOperationResult<TaskMutationResponse>.Success(response);
    }
}
