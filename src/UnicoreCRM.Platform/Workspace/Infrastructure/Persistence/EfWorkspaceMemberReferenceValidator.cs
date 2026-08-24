using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed class EfWorkspaceMemberReferenceValidator(WorkspaceDbContext dbContext)
    : IWorkspaceMemberReferenceValidator, ITrustedWorkspaceMemberResolver
{
    public Task<bool> IsActiveMemberAsync(
        string workspaceId,
        string memberId,
        CancellationToken cancellationToken) =>
        dbContext.Memberships
            .AsNoTracking()
            .AnyAsync(
                membership => membership.WorkspaceId == workspaceId
                              && membership.MemberId == memberId
                              && membership.Status == WorkspaceMembershipStatus.Active,
                cancellationToken);

    public Task<TrustedWorkspaceContext?> ResolveActiveMemberAsync(
        string workspaceId,
        string memberId,
        CancellationToken cancellationToken) =>
        dbContext.Memberships
            .AsNoTracking()
            .Where(membership => membership.WorkspaceId == workspaceId
                                 && membership.MemberId == memberId
                                 && membership.Status == WorkspaceMembershipStatus.Active)
            .Select(membership => new TrustedWorkspaceContext(
                membership.WorkspaceId,
                membership.AccountId,
                membership.MemberId,
                membership.MembershipId))
            .SingleOrDefaultAsync(cancellationToken);
}
