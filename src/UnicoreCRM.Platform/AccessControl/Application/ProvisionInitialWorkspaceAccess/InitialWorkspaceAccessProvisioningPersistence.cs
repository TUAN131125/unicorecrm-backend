using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.ProvisionInitialWorkspaceAccess;

/// <summary>
/// The owner-local persistence surface used only by the initial Workspace access assignment.
/// </summary>
internal interface IInitialWorkspaceAccessPersistence
{
    Task<AccessRole?> FindRoleAsync(string workspaceId, string roleName, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ReadRoleCapabilitiesAsync(string roleId, CancellationToken cancellationToken);
    Task<MembershipRoleAssignment?> FindAssignmentAsync(string workspaceId, string membershipId, string roleId, CancellationToken cancellationToken);

    /// <summary>
    /// Commits the optional role definition, its capabilities and the membership assignment in
    /// one owner-local transaction. It returns <c>false</c> when a uniqueness constraint rejected
    /// the write, which is the concurrent double-submit signal, and leaves no partial state.
    /// </summary>
    Task<bool> TryCommitAsync(
        AccessRole? role,
        IReadOnlyList<RoleCapability> capabilities,
        MembershipRoleAssignment? assignment,
        CancellationToken cancellationToken);
}
