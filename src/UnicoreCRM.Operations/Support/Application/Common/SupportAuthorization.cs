using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Support.Application.Common;

/// <summary>
/// The Support application boundary of the trusted authority chain: authenticated user ->
/// requested Workspace -> verified membership -> trusted CurrentWorkspace -> capability
/// authorization -> Support use case. A caller-supplied Workspace identifier is never trusted
/// here; only the resolved <see cref="TrustedWorkspaceContext"/> is.
/// </summary>
internal sealed class SupportAuthorization(
    ICurrentWorkspace currentWorkspace,
    IAccessAuthorizer accessAuthorizer)
{
    internal async Task<SupportOperationResult<TrustedWorkspaceContext>> AuthorizeAsync(
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!currentWorkspace.IsResolved)
            return SupportOperationResult<TrustedWorkspaceContext>.Failure(SupportErrors.WorkspaceMismatch());

        var decision = await accessAuthorizer.AuthorizeAsync(requirement, correlationId, cancellationToken);
        return decision.IsAllowed
            ? SupportOperationResult<TrustedWorkspaceContext>.Success(currentWorkspace.Require())
            : SupportOperationResult<TrustedWorkspaceContext>.Failure(
                decision.Code == "WORKSPACE_MISMATCH" ? SupportErrors.WorkspaceMismatch() : SupportErrors.AccessDenied());
    }
}
