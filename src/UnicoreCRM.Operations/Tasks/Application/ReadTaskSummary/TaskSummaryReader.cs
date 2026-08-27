using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;

using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.ReadTaskSummary;

/// <summary>
/// The minimized Task projection AI reads through. It carried its own copy of the record-scope and
/// field-visibility rules, which made it a second authorization authority over the same stored
/// policy; it now goes through the canonical AccessControl boundary like every other Tasks use case,
/// so one authority decides and this reader only applies the result.
/// </summary>
internal sealed class TaskSummaryReader(
    TaskAuthorization authorization,
    ITasksPersistence persistence,
    TimeProvider timeProvider) : ITaskSummaryReader
{
    /// <summary>
    /// The representation this reader returns. Every property of <c>TaskSummaryProjection</c> except the identifier is
    /// declared nullable by that contract, so each of these fields genuinely has an admitted absent
    /// representation here even where the module's full read model makes it required. The set is a
    /// fixed static declaration owned by this operation, never assembled per request, and it can
    /// only turn a refusal into a withheld value - never a withheld value into a returned one.
    /// </summary>
    private static readonly RecordAccessRepresentation Representation =
        RecordAccessRepresentation.Create("task.summary", "title", "status", "priority", "dueAt");

    public async Task<TaskSummaryReadResult> ReadAsync(
        string taskId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var metadata = new TaskRequestMetadata(requestId, correlationId);
        // Every field of the minimized summary contract is optional, so this operation can return
        // any of them absent. The full Task read model makes some of them required, but that
        // declaration governs the full representation, not this one.
        var access = await authorization.AuthorizeAsync(
            TaskCapabilities.Read, metadata, cancellationToken, Representation);
        if (!access.IsSuccess)
        {
            return new(access.Error!.Code == "WORKSPACE_MISMATCH"
                ? TaskSummaryReadStatus.WorkspaceMismatch
                : TaskSummaryReadStatus.AccessDenied);
        }

        if (!TaskValidation.IsEntityId(taskId))
            return new(TaskSummaryReadStatus.InvalidReference);

        var trusted = access.Value!.Trusted;
        var task = await persistence.ReadTaskAsync(trusted.WorkspaceId, taskId, cancellationToken);
        if (task is null)
            return new(TaskSummaryReadStatus.NotFound);
        if (await authorization.EnforceRecordAsync(access.Value!, task, "readTaskSummary", metadata, cancellationToken) is not null)
            return new(TaskSummaryReadStatus.NotFound);

        // The projection is the canonical field-enforced one, then narrowed to the fixed minimized
        // shape this contract exposes. A field AccessControl withheld is already absent.
        var document = TaskFieldSecurity.Project(TaskProjection.Task(task), access.Value!.Authorization);
        var summary = new TaskSummaryProjection(
            task.TaskId,
            access.Value!.Authorization.CanRead("title") ? document.Title : null,
            access.Value!.Authorization.CanRead("status") ? document.Status : null,
            access.Value!.Authorization.CanRead("priority") ? document.Priority : null,
            access.Value!.Authorization.CanRead("dueAt") ? document.DueAt : null);

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
}
