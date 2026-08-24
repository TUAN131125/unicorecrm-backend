using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed class EfDevelopmentWorkspaceReferenceLookup(WorkspaceDbContext dbContext) : IDevelopmentWorkspaceReferenceLookup
{
    public Task<DevelopmentWorkspaceReference?> FindActiveMembershipAsync(
        string workspaceKey,
        string accountId,
        string memberId,
        CancellationToken cancellationToken) =>
        (from workspace in dbContext.Workspaces.AsNoTracking()
         join membership in dbContext.Memberships.AsNoTracking() on workspace.WorkspaceId equals membership.WorkspaceId
         where workspace.Key == workspaceKey
               && membership.AccountId == accountId
               && membership.MemberId == memberId
               && membership.Status == WorkspaceMembershipStatus.Active
         select new DevelopmentWorkspaceReference(workspace.WorkspaceId, membership.MembershipId))
        .SingleOrDefaultAsync(cancellationToken);
}
