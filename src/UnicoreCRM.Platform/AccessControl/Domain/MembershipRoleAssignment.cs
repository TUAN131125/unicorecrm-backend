namespace UnicoreCRM.Platform.AccessControl.Domain;

internal sealed class MembershipRoleAssignment
{
    private MembershipRoleAssignment() { }

    internal MembershipRoleAssignment(string workspaceId, string membershipId, string roleId, DateTimeOffset now)
    {
        AssignmentId = AccessControlIds.New("assignment");
        WorkspaceId = workspaceId;
        MembershipId = membershipId;
        RoleId = roleId;
        AssignedAt = now;
    }

    public string AssignmentId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string MembershipId { get; private set; } = null!;
    public string RoleId { get; private set; } = null!;
    public DateTimeOffset AssignedAt { get; private set; }
}
