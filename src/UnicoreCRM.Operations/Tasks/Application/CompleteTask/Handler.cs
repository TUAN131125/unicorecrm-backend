using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.CompleteTask;

internal sealed record Command(string TaskId, CompleteTaskRequest Request, TaskCommandMetadata Metadata);

internal sealed class Handler(TaskAuthorization authorization, TaskMutationExecution execution)
{
    internal async Task<TaskOperationResult<TaskMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(TaskCapabilities.Complete, command.Metadata.CorrelationId, cancellationToken);
        if (!access.IsSuccess)
            return TaskOperationResult<TaskMutationResponse>.Failure(access.Error!);
        var outcome = TaskValidation.RequiredText(command.Request.Outcome, "outcome", 4000, out var fields);
        if (outcome is null)
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.Validation(fields));
        var fingerprint = TaskCommandSupport.Fingerprint(new { command.TaskId, outcome, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "completeTask",
            "TASK_COMPLETED",
            command.TaskId,
            command.Metadata,
            fingerprint,
            (task, now) => task.Complete(outcome, now),
            null,
            cancellationToken);
    }
}
