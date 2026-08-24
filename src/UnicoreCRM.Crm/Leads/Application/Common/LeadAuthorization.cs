using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.Common;

internal sealed class LeadAuthorization(
    ICurrentWorkspace currentWorkspace,
    IAccessAuthorizer accessAuthorizer)
{
    internal async Task<LeadOperationResult<TrustedWorkspaceContext>> AuthorizeAsync(
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!currentWorkspace.IsResolved)
            return LeadOperationResult<TrustedWorkspaceContext>.Failure(LeadErrors.WorkspaceMismatch());

        var decision = await accessAuthorizer.AuthorizeAsync(requirement, correlationId, cancellationToken);
        return decision.IsAllowed
            ? LeadOperationResult<TrustedWorkspaceContext>.Success(currentWorkspace.Require())
            : LeadOperationResult<TrustedWorkspaceContext>.Failure(
                decision.Code == "WORKSPACE_MISMATCH" ? LeadErrors.WorkspaceMismatch() : LeadErrors.AccessDenied());
    }
}
