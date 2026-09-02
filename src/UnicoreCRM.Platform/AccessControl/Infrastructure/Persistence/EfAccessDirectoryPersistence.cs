using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.AccessControl.Application.AccessDirectory;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

internal sealed class EfAccessDirectoryPersistence(AccessControlDbContext dbContext) : IAccessDirectoryPersistence
{
    public async Task<AccessControlDirectoryState> ReadDirectoryAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        var revision = await dbContext.WorkspaceDirectoryRevisions
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .Select(item => (long?)item.Revision)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
        var roles = await dbContext.Roles
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .ToArrayAsync(cancellationToken);
        var roleIds = roles.Select(item => item.RoleId).ToArray();
        var capabilities = await dbContext.RoleCapabilities
            .AsNoTracking()
            .Where(item => roleIds.Contains(item.RoleId))
            .ToArrayAsync(cancellationToken);
        var assignments = await dbContext.MembershipRoleAssignments
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .ToArrayAsync(cancellationToken);
        var scopes = await dbContext.RoleDataScopes
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .ToArrayAsync(cancellationToken);
        var fields = await dbContext.RoleFieldSecurity
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .ToArrayAsync(cancellationToken);
        return new AccessControlDirectoryState(revision, roles, capabilities, assignments, scopes, fields);
    }

    public async Task AppendReadEvidenceAsync(
        AccessDirectoryReadEvidence evidence,
        CancellationToken cancellationToken)
    {
        dbContext.AccessDirectoryReadEvidence.Add(evidence);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
