namespace UnicoreCRM.Platform.Workspace.Domain;

internal sealed class WorkspaceMembership
{
    private WorkspaceMembership() { }

    internal WorkspaceMembership(string workspaceId, string accountId, string memberId, DateTimeOffset now)
    {
        MembershipId = WorkspaceIds.New("wsm");
        WorkspaceId = workspaceId;
        AccountId = accountId;
        MemberId = memberId;
        Status = WorkspaceMembershipStatus.Active;
        CreatedAt = now;
    }

    public string MembershipId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string AccountId { get; private set; } = null!;
    public string MemberId { get; private set; } = null!;
    public WorkspaceMembershipStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

internal enum WorkspaceMembershipStatus
{
    Active,
    Suspended
}
