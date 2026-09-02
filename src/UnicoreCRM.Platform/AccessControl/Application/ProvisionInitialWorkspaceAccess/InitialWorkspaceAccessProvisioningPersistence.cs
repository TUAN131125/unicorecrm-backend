using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.ProvisionInitialWorkspaceAccess;

/// <summary>
/// The owner-local persistence surface used only by the initial Workspace access assignment.
/// </summary>
internal sealed record InitialWorkspaceAccessAnchor(MembershipRoleAssignment Assignment, AccessRole Role);

internal interface IInitialWorkspaceAccessPersistence
{
    /// <summary>
    /// The canonical provisioning anchor: the role this membership is already assigned to inside
    /// AccessControl. It is identity-based, so it keeps working after an admitted
    /// <c>replaceAccessRole</c> has legitimately changed the role's name, description, template
    /// provenance or version, and it can never select an unrelated role that merely carries the
    /// seeded display name.
    /// </summary>
    Task<InitialWorkspaceAccessAnchor?> FindAssignedRoleAsync(
        string workspaceId,
        string membershipId,
        CancellationToken cancellationToken);

    Task<AccessRole?> FindRoleAsync(string workspaceId, string roleName, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ReadRoleCapabilitiesAsync(string roleId, CancellationToken cancellationToken);
    Task<MembershipRoleAssignment?> FindAssignmentAsync(string workspaceId, string membershipId, string roleId, CancellationToken cancellationToken);

    /// <summary>
    /// Commits the optional role definition, capability additions and membership assignment in one
    /// owner-local transaction. It returns <c>false</c> when a uniqueness constraint rejected the
    /// write, which is the concurrent retry signal, and leaves no partial state.
    /// </summary>
    Task<bool> TryCommitAsync(
        AccessRole? role,
        IReadOnlyList<RoleCapability> capabilities,
        MembershipRoleAssignment? assignment,
        CancellationToken cancellationToken);
}
