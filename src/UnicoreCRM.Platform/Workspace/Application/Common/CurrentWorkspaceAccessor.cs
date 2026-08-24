using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.Workspace.Application.Common;

internal sealed class CurrentWorkspaceAccessor : ICurrentWorkspace, ITrustedWorkspaceSetter
{
    private TrustedWorkspaceContext? current;

    public bool IsResolved => current is not null;

    public TrustedWorkspaceContext Require() =>
        current ?? throw new InvalidOperationException("A trusted workspace has not been resolved for this request.");

    public void Set(TrustedWorkspaceContext context)
    {
        if (current is not null && current != context)
            throw new InvalidOperationException("The trusted workspace cannot change during a request.");
        current = context;
    }
}
