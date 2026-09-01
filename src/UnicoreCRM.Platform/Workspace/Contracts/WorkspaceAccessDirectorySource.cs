namespace UnicoreCRM.Platform.Workspace.Contracts;

public interface IWorkspaceAccessDirectorySource
{
    Task<WorkspaceAccessDirectorySnapshot?> ReadAsync(string workspaceId, CancellationToken cancellationToken);
}

public sealed record WorkspaceAccessDirectorySnapshot(
    string WorkspaceId,
    string WorkspaceKey,
    string Name,
    string LogoText,
    IReadOnlyList<WorkspaceAccessDirectoryMembership> Memberships,
    IReadOnlyList<WorkspaceAccessDirectoryInvitation> Invitations);

public sealed record WorkspaceAccessDirectoryMembership(
    string MembershipId,
    string? AccountId,
    string MemberId,
    string Status,
    string Source,
    DateTimeOffset? CreatedAt,
    long Version,
    IReadOnlyList<string> TeamIds);

public sealed record WorkspaceAccessDirectoryInvitation(
    string InvitationId,
    string MembershipId,
    string WorkspaceId,
    string Email,
    string DisplayName,
    IReadOnlyList<string> RoleIds,
    IReadOnlyList<string> TeamIds,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSentAt,
    DateTimeOffset ExpiresAt,
    long Version,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RevokedAt);
