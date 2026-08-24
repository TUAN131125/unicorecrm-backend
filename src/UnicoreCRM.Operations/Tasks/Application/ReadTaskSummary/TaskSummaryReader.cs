using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.ReadTaskSummary;

internal sealed class TaskSummaryReader(
    ICurrentWorkspace currentWorkspace,
    IAccessAuthorizer accessAuthorizer,
    ITasksPersistence persistence,
    TimeProvider timeProvider) : ITaskSummaryReader
{
    public async Task<TaskSummaryReadResult> ReadAsync(
        string taskId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!currentWorkspace.IsResolved)
            return new(TaskSummaryReadStatus.WorkspaceMismatch);

        var access = await accessAuthorizer.AuthorizeAsync(TaskCapabilities.Read, correlationId, cancellationToken);
        if (!access.IsAllowed)
        {
            return new(access.Code == "WORKSPACE_MISMATCH"
                ? TaskSummaryReadStatus.WorkspaceMismatch
                : TaskSummaryReadStatus.AccessDenied);
        }

        if (!TaskValidation.IsEntityId(taskId))
            return new(TaskSummaryReadStatus.InvalidReference);

        var trusted = currentWorkspace.Require();
        var task = await persistence.ReadTaskAsync(trusted.WorkspaceId, taskId, cancellationToken);
        if (task is null || !CanReadRecord(access.Context!, trusted.MemberId, task.AssigneeId))
            return new(TaskSummaryReadStatus.NotFound);

        var document = TaskProjection.Task(task);
        var summary = new TaskSummaryProjection(
            task.TaskId,
            Visible(access.Context!, "title") ? document.Title : null,
            Visible(access.Context!, "status") ? document.Status : null,
            Visible(access.Context!, "priority") ? document.Priority : null,
            Visible(access.Context!, "dueAt") ? document.DueAt : null);

        persistence.AddAudit(new TaskAuditRecord(
            "readTaskSummary",
            trusted.WorkspaceId,
            trusted.MemberId,
            task.TaskId,
            requestId,
            correlationId,
            "READ",
            task.Version,
            task.Version,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return new(TaskSummaryReadStatus.Succeeded, summary);
    }

    private static bool CanReadRecord(AuthorizationContextDocument context, string memberId, string assigneeId)
    {
        var scope = context.DataScopes.FirstOrDefault(item =>
            string.Equals(item.ResourceKey, "tasks", StringComparison.OrdinalIgnoreCase));
        return scope?.Scope.ToUpperInvariant() switch
        {
            null or "WORKSPACE" => true,
            "OWN" => string.Equals(memberId, assigneeId, StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool Visible(AuthorizationContextDocument context, string fieldKey)
    {
        var field = context.FieldSecurity.FirstOrDefault(item =>
            string.Equals(item.ResourceKey, "tasks", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase));
        return field is null || field.Access is "READ_ONLY" or "READ_WRITE";
    }
}
