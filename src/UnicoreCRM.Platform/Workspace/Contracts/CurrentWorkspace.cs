namespace UnicoreCRM.Platform.Workspace.Contracts;

/// <summary>
/// The narrow cross-owner Workspace surface. A trusted workspace exists only after an
/// authenticated account has been matched to an active membership; foreign owners consume
/// the resolved value and never resolve workspace authority themselves.
/// </summary>
public sealed record TrustedWorkspaceContext(
    string WorkspaceId,
    string AccountId,
    string MemberId,
    string MembershipId);

public interface ICurrentWorkspace
{
    bool IsResolved { get; }
    TrustedWorkspaceContext Require();
}
