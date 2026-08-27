using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.ArchiveTask;

internal sealed record Command(string TaskId, ArchiveTaskRequest Request, TaskCommandMetadata Metadata);

internal sealed class Handler(TaskAuthorization authorization, TaskMutationExecution execution)
{
    internal async Task<TaskOperationResult<TaskMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var metadata = new TaskRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(TaskCapabilities.Update, metadata, cancellationToken);
        if (!access.IsSuccess)
            return TaskOperationResult<TaskMutationResponse>.Failure(access.Error!);
        var reason = TaskValidation.RequiredText(command.Request.Reason, "reason", 2000, out var fields);
        if (reason is null)
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.Validation(fields));
        var fingerprint = TaskCommandSupport.Fingerprint(new { command.TaskId, reason, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "archiveTask",
            "TASK_ARCHIVED",
            command.TaskId,
            command.Metadata,
            fingerprint,
            (task, now) => { task.Archive(reason, now); return true; },
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "archiveTask", metadata, cancellationToken, "archivedAt", "archiveReason"),
            cancellationToken);
    }
}
