using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.AccessControl.Application.ProvisionInitialWorkspaceAccess;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

internal sealed class EfInitialWorkspaceAccessPersistence(AccessControlDbContext dbContext)
    : IInitialWorkspaceAccessPersistence
{
    private const int DuplicateKey = 2601;
    private const int UniqueConstraint = 2627;

    public Task<AccessRole?> FindRoleAsync(string workspaceId, string roleName, CancellationToken cancellationToken) =>
        dbContext.Roles
            .AsNoTracking()
            .SingleOrDefaultAsync(role => role.WorkspaceId == workspaceId && role.Name == roleName, cancellationToken);

    public async Task<IReadOnlyList<string>> ReadRoleCapabilitiesAsync(string roleId, CancellationToken cancellationToken) =>
        await dbContext.RoleCapabilities
            .AsNoTracking()
            .Where(capability => capability.RoleId == roleId)
            .Select(capability => capability.Capability)
            .OrderBy(capability => capability)
            .ToArrayAsync(cancellationToken);

    public Task<MembershipRoleAssignment?> FindAssignmentAsync(
        string workspaceId,
        string membershipId,
        string roleId,
        CancellationToken cancellationToken) =>
        dbContext.MembershipRoleAssignments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                assignment => assignment.WorkspaceId == workspaceId
                              && assignment.MembershipId == membershipId
                              && assignment.RoleId == roleId,
                cancellationToken);

    public async Task<bool> TryCommitAsync(
        AccessRole? role,
        IReadOnlyList<RoleCapability> capabilities,
        MembershipRoleAssignment? assignment,
        CancellationToken cancellationToken)
    {
        if (role is null && assignment is null)
            return true;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        if (role is not null)
        {
            dbContext.Roles.Add(role);
            dbContext.RoleCapabilities.AddRange(capabilities);
        }
        if (assignment is not null)
            dbContext.MembershipRoleAssignments.Add(assignment);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            // Only a uniqueness violation is a convergence signal. Anything else is a real fault.
            if (exception.InnerException is not SqlException { Number: DuplicateKey or UniqueConstraint })
                throw;
            return false;
        }
    }
}
