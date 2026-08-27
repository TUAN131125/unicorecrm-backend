using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.RescheduleTask;

internal sealed record Command(string TaskId, RescheduleTaskRequest Request, TaskCommandMetadata Metadata);

internal sealed class Handler(TaskAuthorization authorization, TaskMutationExecution execution)
{
    internal async Task<TaskOperationResult<TaskMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var metadata = new TaskRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(TaskCapabilities.Update, metadata, cancellationToken);
        if (!access.IsSuccess)
            return TaskOperationResult<TaskMutationResponse>.Failure(access.Error!);
        var dueAt = TaskValidation.RequiredUtc(command.Request.DueAt, "dueAt", out var fields);
        if (dueAt is null)
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.Validation(fields));
        var fingerprint = TaskCommandSupport.Fingerprint(new { command.TaskId, dueAt, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "rescheduleTask",
            "TASK_RESCHEDULED",
            command.TaskId,
            command.Metadata,
            fingerprint,
            (task, now) => task.Reschedule(dueAt.Value, now),
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "rescheduleTask", metadata, cancellationToken),
            recordAccess => TaskAuthorization.EnforceFieldWrite(recordAccess, "dueAt"),
            cancellationToken);
    }
}
