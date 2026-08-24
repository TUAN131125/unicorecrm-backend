namespace UnicoreCRM.Platform.Workspace.Contracts;

public interface IWorkspaceMemberReferenceValidator
{
    Task<bool> IsActiveMemberAsync(
        string workspaceId,
        string memberId,
        CancellationToken cancellationToken);
}

public interface ITrustedWorkspaceMemberResolver
{
    Task<TrustedWorkspaceContext?> ResolveActiveMemberAsync(
        string workspaceId,
        string memberId,
        CancellationToken cancellationToken);
}
