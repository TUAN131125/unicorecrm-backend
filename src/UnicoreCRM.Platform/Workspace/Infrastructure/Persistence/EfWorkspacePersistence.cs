using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.Workspace.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed class EfWorkspacePersistence(WorkspaceDbContext dbContext) : IWorkspacePersistence
{
    public async Task<IReadOnlyList<WorkspaceMembershipReadModel>> ListMembershipsAsync(
        string accountId,
        string memberId,
        CancellationToken cancellationToken) =>
        await (from membership in dbContext.Memberships.AsNoTracking()
               join workspace in dbContext.Workspaces.AsNoTracking() on membership.WorkspaceId equals workspace.WorkspaceId
               where membership.AccountId == accountId
                     && membership.MemberId == memberId
               orderby workspace.Name, workspace.WorkspaceId
               select new WorkspaceMembershipReadModel(
                   membership.MembershipId,
                   workspace.WorkspaceId,
                   workspace.Key,
                   workspace.Name,
                   membership.Status == WorkspaceMembershipStatus.Active ? "active" : "suspended",
                   workspace.LogoText))
            .Take(200)
            .ToListAsync(cancellationToken);

    public Task<WorkspaceBootstrapReadModel?> FindActiveBootstrapAsync(
        string accountId,
        string memberId,
        string workspaceId,
        CancellationToken cancellationToken) =>
        (from membership in dbContext.Memberships.AsNoTracking()
         join workspace in dbContext.Workspaces.AsNoTracking() on membership.WorkspaceId equals workspace.WorkspaceId
         join bootstrap in dbContext.BootstrapProjections.AsNoTracking() on workspace.WorkspaceId equals bootstrap.WorkspaceId
         where membership.AccountId == accountId
               && membership.MemberId == memberId
               && membership.WorkspaceId == workspaceId
               && membership.Status == WorkspaceMembershipStatus.Active
         select new WorkspaceBootstrapReadModel(
             new WorkspaceMembershipReadModel(
                 membership.MembershipId,
                 workspace.WorkspaceId,
                 workspace.Key,
                 workspace.Name,
                 "active",
                 workspace.LogoText),
             bootstrap.ContextVersion,
             bootstrap.ConfigurationVersion,
             bootstrap.Locale,
             bootstrap.TimeZone,
             bootstrap.BaseCurrency,
             bootstrap.CapabilitiesJson,
             bootstrap.EnabledModuleKeysJson,
             bootstrap.AvailableProductSpacesJson))
        .SingleOrDefaultAsync(cancellationToken);

    public Task<TrustedWorkspaceContext?> ResolveActiveAsync(
        string accountId,
        string memberId,
        string workspaceId,
        CancellationToken cancellationToken) =>
        dbContext.Memberships
            .AsNoTracking()
            .Where(membership =>
                membership.AccountId == accountId
                && membership.MemberId == memberId
                && membership.WorkspaceId == workspaceId
                && membership.Status == WorkspaceMembershipStatus.Active)
            .Select(membership => new TrustedWorkspaceContext(
                membership.WorkspaceId,
                membership.AccountId,
                membership.MemberId,
                membership.MembershipId))
            .SingleOrDefaultAsync(cancellationToken);

    public void AddAccessRecord(WorkspaceAccessRecord record) => dbContext.AccessRecords.Add(record);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
