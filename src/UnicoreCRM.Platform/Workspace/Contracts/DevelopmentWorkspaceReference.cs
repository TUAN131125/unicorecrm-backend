namespace UnicoreCRM.Platform.Workspace.Contracts;

internal interface IDevelopmentWorkspaceReferenceLookup
{
    Task<DevelopmentWorkspaceReference?> FindActiveMembershipAsync(
        string workspaceKey,
        string accountId,
        string memberId,
        CancellationToken cancellationToken);
}

internal sealed record DevelopmentWorkspaceReference(string WorkspaceId, string MembershipId);
