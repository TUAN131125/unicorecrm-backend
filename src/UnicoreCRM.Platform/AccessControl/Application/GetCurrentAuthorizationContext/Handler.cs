using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.GetCurrentAuthorizationContext;

internal sealed class Handler(IAccessAuthorizer authorizer)
{
    internal async Task<AccessOperationResult<AuthorizationContextDocument>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var decision = await authorizer.AuthorizeAsync(
            AccessCapabilities.WorkspaceContextResolve,
            query.CorrelationId,
            cancellationToken);
        if (decision.IsAllowed && decision.Context is { } context)
            return AccessOperationResult<AuthorizationContextDocument>.Success(context);
        return AccessOperationResult<AuthorizationContextDocument>.Failure(
            decision.Code == "WORKSPACE_MISMATCH" ? AccessErrors.WorkspaceMismatch() : AccessErrors.AccessDenied());
    }
}
