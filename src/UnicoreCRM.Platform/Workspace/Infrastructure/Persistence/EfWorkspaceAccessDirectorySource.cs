using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed class EfWorkspaceAccessDirectorySource(WorkspaceDbContext dbContext)
    : IWorkspaceAccessDirectorySource
{
    public async Task<WorkspaceAccessDirectorySnapshot?> ReadAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        var workspace = await dbContext.Workspaces
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .Select(item => new { item.WorkspaceId, WorkspaceKey = item.Key, item.Name, item.LogoText })
            .SingleOrDefaultAsync(cancellationToken);
        if (workspace is null)
            return null;

        var storedMemberships = await dbContext.Memberships
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .OrderBy(item => item.MembershipId)
            .Select(item => new
            {
                item.MembershipId,
                item.AccountId,
                item.MemberId,
                item.Status,
                item.CreatedAt
            })
            .ToArrayAsync(cancellationToken);

        var memberships = storedMemberships
            .Select(item => new WorkspaceAccessDirectoryMembership(
                item.MembershipId,
                item.AccountId,
                item.MemberId,
                item.Status == WorkspaceMembershipStatus.Active ? "active" : "suspended",
                "direct_provisioning",
                item.CreatedAt,
                0,
                []))
            .ToArray();

        // No invitation or membership-team mutation is admitted in the current runtime. The
        // owner snapshot therefore authoritatively exposes the retained sets as empty rather than
        // allowing AccessControl to infer or persist foreign facts.
        return new WorkspaceAccessDirectorySnapshot(
            workspace.WorkspaceId,
            workspace.WorkspaceKey,
            workspace.Name,
            workspace.LogoText,
            memberships,
            []);
    }
}
