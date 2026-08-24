using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.Common;

internal sealed class TaskAuthorization(
    ICurrentWorkspace currentWorkspace,
    IAccessAuthorizer accessAuthorizer)
{
    internal async Task<TaskOperationResult<TrustedWorkspaceContext>> AuthorizeAsync(
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!currentWorkspace.IsResolved)
            return TaskOperationResult<TrustedWorkspaceContext>.Failure(TaskErrors.WorkspaceMismatch());

        var decision = await accessAuthorizer.AuthorizeAsync(requirement, correlationId, cancellationToken);
        return decision.IsAllowed
            ? TaskOperationResult<TrustedWorkspaceContext>.Success(currentWorkspace.Require())
            : TaskOperationResult<TrustedWorkspaceContext>.Failure(
                decision.Code == "WORKSPACE_MISMATCH" ? TaskErrors.WorkspaceMismatch() : TaskErrors.AccessDenied());
    }
}
