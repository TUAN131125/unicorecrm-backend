using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.ProvisionInitialWorkspaceAccess;

/// <summary>
/// The AccessControl-owned participant of the Initial Workspace Provisioning workflow. It is
/// convergent: repeated calls for the same Workspace and membership reach the same single role
/// and single assignment, so a workflow retry completes an interrupted provisioning without
/// producing duplicate authority.
/// </summary>
internal sealed class InitialWorkspaceAccessProvisioningService(
    IInitialWorkspaceAccessPersistence persistence,
    TimeProvider timeProvider) : IInitialWorkspaceAccessProvisioning
{
    private const int ConvergenceAttempts = 2;

    public async Task<InitialWorkspaceAccessResult> EnsureInitialWorkspaceAccessAsync(
        string workspaceId,
        string membershipId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(membershipId);
        var capabilities = InitialWorkspaceAccessPolicy.Validated();

        for (var attempt = 0; attempt < ConvergenceAttempts; attempt++)
        {
            var existingRole = await persistence.FindRoleAsync(workspaceId, InitialWorkspaceAccessPolicy.RoleName, cancellationToken);
            if (existingRole is not null)
            {
                if (!InitialWorkspaceAccessPolicy.HasCanonicalRoleIdentity(existingRole, workspaceId))
                    throw new InvalidOperationException("The existing initial Workspace role identity does not match the server-owned definition.");

                var storedCapabilities = await persistence.ReadRoleCapabilitiesAsync(existingRole.RoleId, cancellationToken);
                var existingAssignment = await persistence.FindAssignmentAsync(workspaceId, membershipId, existingRole.RoleId, cancellationToken);
                if (storedCapabilities.SequenceEqual(capabilities, StringComparer.Ordinal))
                {
                    if (existingAssignment is not null)
                    {
                        return new InitialWorkspaceAccessResult(
                            InitialWorkspaceAccessStatus.AlreadyAssigned,
                            existingRole.RoleId,
                            existingAssignment.AssignmentId,
                            capabilities);
                    }
                }
                else
                {
                    // An upgrade requires the existing creator assignment as the provisioning
                    // anchor inside AccessControl. A name match or arbitrary partial set is not
                    // sufficient evidence that this is the server-owned initial role.
                    if (existingAssignment is null
                        || !InitialWorkspaceAccessPolicy.IsKnownPreviousCapabilitySet(storedCapabilities))
                    {
                        throw new InvalidOperationException("The existing initial Workspace role does not match a server-owned capability set admitted for upgrade.");
                    }

                    var addedCapabilities = capabilities
                        .Except(storedCapabilities, StringComparer.Ordinal)
                        .Select(capability => new RoleCapability(existingRole.RoleId, capability))
                        .ToArray();
                    if (await persistence.TryCommitAsync(null, addedCapabilities, null, cancellationToken))
                    {
                        return new InitialWorkspaceAccessResult(
                            InitialWorkspaceAccessStatus.Assigned,
                            existingRole.RoleId,
                            existingAssignment.AssignmentId,
                            capabilities);
                    }
                    continue;
                }
            }

            var now = timeProvider.GetUtcNow();
            var role = existingRole ?? new AccessRole(
                workspaceId,
                InitialWorkspaceAccessPolicy.RoleName,
                InitialWorkspaceAccessPolicy.RoleDescription,
                null,
                now);
            var roleCapabilities = existingRole is null
                ? capabilities.Select(capability => new RoleCapability(role.RoleId, capability)).ToArray()
                : [];
            var assignment = new MembershipRoleAssignment(workspaceId, membershipId, role.RoleId, now);
            if (await persistence.TryCommitAsync(existingRole is null ? role : null, roleCapabilities, assignment, cancellationToken))
                return new InitialWorkspaceAccessResult(InitialWorkspaceAccessStatus.Assigned, role.RoleId, assignment.AssignmentId, capabilities);
        }

        throw new InvalidOperationException("Initial Workspace access provisioning did not converge on a single assignment.");
    }
}
