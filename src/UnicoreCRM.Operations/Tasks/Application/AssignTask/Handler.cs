using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.AssignTask;

internal sealed record Command(string TaskId, AssignTaskRequest Request, TaskCommandMetadata Metadata);

internal sealed class Handler(
    TaskAuthorization authorization,
    TaskMutationExecution execution,
    IWorkspaceMemberReferenceValidator memberValidator)
{
    internal async Task<TaskOperationResult<TaskMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var metadata = new TaskRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(TaskCapabilities.Assign, metadata, cancellationToken);
        if (!access.IsSuccess)
            return TaskOperationResult<TaskMutationResponse>.Failure(access.Error!);
        var assigneeId = TaskValidation.RequiredEntity(command.Request.AssigneeId, "assigneeId", out var fields);
        if (assigneeId is null)
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.Validation(fields));
        var fingerprint = TaskCommandSupport.Fingerprint(new { command.TaskId, assigneeId, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "assignTask",
            "TASK_ASSIGNED",
            command.TaskId,
            command.Metadata,
            fingerprint,
            (task, now) => task.Assign(assigneeId, now),
            async (trusted, token) => await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, assigneeId, token)
                ? null
                : TaskErrors.Validation(new Dictionary<string, string[]>
                {
                    ["assigneeId"] = ["assigneeId must reference an active member of the trusted workspace."]
                }),
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "assignTask", metadata, cancellationToken, "assigneeId"),
            cancellationToken);
    }
}
