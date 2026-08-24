using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.Workspace.Application.Common;
using UnicoreCRM.Platform.Workspace.Application.ProvisionInitialWorkspace;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed class EfInitialWorkspaceProvisioningPersistence(WorkspaceDbContext dbContext)
    : IInitialWorkspaceProvisioningPersistence
{
    private const int DuplicateKey = 2601;
    private const int UniqueConstraint = 2627;

    public Task<InitialWorkspaceProvisioningRecord?> FindProvisioningRecordAsync(
        string accountId,
        CancellationToken cancellationToken) =>
        dbContext.InitialProvisioningRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.AccountId == accountId, cancellationToken);

    public Task<bool> HasActiveMembershipAsync(string accountId, CancellationToken cancellationToken) =>
        dbContext.Memberships
            .AsNoTracking()
            .AnyAsync(
                membership => membership.AccountId == accountId && membership.Status == WorkspaceMembershipStatus.Active,
                cancellationToken);

    public Task<bool> WorkspaceKeyExistsAsync(string workspaceKey, CancellationToken cancellationToken) =>
        dbContext.Workspaces.AsNoTracking().AnyAsync(workspace => workspace.Key == workspaceKey, cancellationToken);

    public Task<WorkspaceMembershipReadModel?> FindMembershipAsync(
        string workspaceId,
        string membershipId,
        CancellationToken cancellationToken) =>
        (from membership in dbContext.Memberships.AsNoTracking()
         join workspace in dbContext.Workspaces.AsNoTracking() on membership.WorkspaceId equals workspace.WorkspaceId
         where membership.WorkspaceId == workspaceId && membership.MembershipId == membershipId
         select new WorkspaceMembershipReadModel(
             membership.MembershipId,
             workspace.WorkspaceId,
             workspace.Key,
             workspace.Name,
             membership.Status == WorkspaceMembershipStatus.Active ? "active" : "suspended",
             workspace.LogoText))
        .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<InitialWorkspaceProvisioningRecord>> ListAccessPendingAsync(
        int limit,
        CancellationToken cancellationToken) =>
        await dbContext.InitialProvisioningRecords
            .AsNoTracking()
            .Where(record => record.State == InitialWorkspaceProvisioningState.AccessPending)
            .OrderBy(record => record.ProvisionedAt)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

    public async Task<bool> TryCompleteProvisioningAsync(
        string accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var record = await dbContext.InitialProvisioningRecords
            .SingleOrDefaultAsync(item => item.AccountId == accountId, cancellationToken);
        if (record is null)
            return false;
        record.MarkCompleted(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> TryCommitProvisioningAsync(
        WorkspaceDefinition workspace,
        WorkspaceMembership membership,
        WorkspaceBootstrapProjection configuration,
        InitialWorkspaceProvisioningRecord provisioning,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        dbContext.Workspaces.Add(workspace);
        dbContext.Memberships.Add(membership);
        dbContext.BootstrapProjections.Add(configuration);
        dbContext.InitialProvisioningRecords.Add(provisioning);
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
