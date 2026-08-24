namespace UnicoreCRM.Platform.AccessControl.Contracts;

/// <summary>
/// The narrow AccessControl participant boundary for the multi-owner Initial Workspace
/// Provisioning workflow. AccessControl remains the sole authority for roles, capabilities and
/// membership assignments: the caller supplies only the Workspace and membership scalar
/// references and can neither name the role nor choose any capability.
/// </summary>
public interface IInitialWorkspaceAccessProvisioning
{
    Task<InitialWorkspaceAccessResult> EnsureInitialWorkspaceAccessAsync(
        string workspaceId,
        string membershipId,
        CancellationToken cancellationToken);
}

public enum InitialWorkspaceAccessStatus
{
    /// <summary>This call created the initial role and/or the creator assignment.</summary>
    Assigned,

    /// <summary>The initial assignment already existed and was left unchanged.</summary>
    AlreadyAssigned
}

public sealed record InitialWorkspaceAccessResult(
    InitialWorkspaceAccessStatus Status,
    string RoleId,
    string AssignmentId,
    IReadOnlyList<string> Capabilities);
