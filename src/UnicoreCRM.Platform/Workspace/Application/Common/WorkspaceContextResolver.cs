using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.Workspace.Application.Common;

internal sealed class WorkspaceContextResolver(IWorkspacePersistence persistence) : IWorkspaceContextResolver
{
    public Task<TrustedWorkspaceContext?> ResolveAsync(
        string accountId,
        string memberId,
        string requestedWorkspaceId,
        CancellationToken cancellationToken) =>
        persistence.ResolveActiveAsync(accountId, memberId, requestedWorkspaceId, cancellationToken);
}
