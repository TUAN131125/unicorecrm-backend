using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.Common;

internal sealed class DealAuthorization(
    ICurrentWorkspace currentWorkspace,
    IAccessAuthorizer accessAuthorizer)
{
    internal async Task<DealOperationResult<TrustedWorkspaceContext>> AuthorizeAsync(
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!currentWorkspace.IsResolved)
            return DealOperationResult<TrustedWorkspaceContext>.Failure(DealErrors.WorkspaceMismatch());

        var decision = await accessAuthorizer.AuthorizeAsync(requirement, correlationId, cancellationToken);
        return decision.IsAllowed
            ? DealOperationResult<TrustedWorkspaceContext>.Success(currentWorkspace.Require())
            : DealOperationResult<TrustedWorkspaceContext>.Failure(
                decision.Code == "WORKSPACE_MISMATCH" ? DealErrors.WorkspaceMismatch() : DealErrors.AccessDenied());
    }
}
