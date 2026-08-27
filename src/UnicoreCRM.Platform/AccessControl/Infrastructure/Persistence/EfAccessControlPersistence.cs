using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

internal sealed class EfAccessControlPersistence(AccessControlDbContext dbContext) : IAccessControlPersistence
{
    public async Task<EffectiveAccessState> LoadEffectiveStateAsync(
        string workspaceId,
        string membershipId,
        CancellationToken cancellationToken)
    {
        var roles = await (from assignment in dbContext.MembershipRoleAssignments.AsNoTracking()
                           join role in dbContext.Roles.AsNoTracking() on assignment.RoleId equals role.RoleId
                           where assignment.WorkspaceId == workspaceId
                                 && assignment.MembershipId == membershipId
                                 && role.WorkspaceId == workspaceId
                                 && role.IsActive
                           orderby role.RoleId
                           select new EffectiveRoleState(role.RoleId, role.SourceTemplateId))
            .Take(101)
            .ToListAsync(cancellationToken);
        if (roles.Count > 100)
            throw new InvalidOperationException("Effective authorization exceeds the contract role limit.");
        if (roles.Count == 0)
            return new EffectiveAccessState([], [], [], []);

        var roleIds = roles.Select(role => role.RoleId).ToArray();
        var capabilities = await dbContext.RoleCapabilities
            .AsNoTracking()
            .Where(item => roleIds.Contains(item.RoleId))
            .Select(item => item.Capability)
            .Distinct()
            .Take(1001)
            .ToListAsync(cancellationToken);
        if (capabilities.Count > 1000)
            throw new InvalidOperationException("Effective authorization exceeds the contract capability limit.");

        var dataScopes = await dbContext.RoleDataScopes
            .AsNoTracking()
            .Where(item => roleIds.Contains(item.RoleId))
            .Select(item => new EffectiveDataScopePolicy(item.ResourceKey, item.Scope))
            .Take(5001)
            .ToListAsync(cancellationToken);
        if (dataScopes.Count > 5000)
            throw new InvalidOperationException("Effective authorization exceeds the contract data-scope limit.");

        var fieldSecurity = await dbContext.RoleFieldSecurity
            .AsNoTracking()
            .Where(item => roleIds.Contains(item.RoleId))
            .Select(item => new EffectiveFieldSecurityPolicy(item.ResourceKey, item.FieldKey, item.Access))
            .Take(10001)
            .ToListAsync(cancellationToken);
        if (fieldSecurity.Count > 10000)
            throw new InvalidOperationException("Effective authorization exceeds the contract field-security limit.");

        return new EffectiveAccessState(roles, capabilities, dataScopes, fieldSecurity);
    }

    public void AddDecision(AuthorizationDecisionRecord decision) => dbContext.AuthorizationDecisions.Add(decision);

    public void AddRecordDecision(RecordAccessDecisionRecord decision) => dbContext.RecordAccessDecisions.Add(decision);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
