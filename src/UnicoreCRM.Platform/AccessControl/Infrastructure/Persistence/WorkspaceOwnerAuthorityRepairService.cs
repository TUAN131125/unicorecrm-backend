using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Platform.AccessControl.Application.ProvisionInitialWorkspaceAccess;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

internal sealed class WorkspaceOwnerAuthorityRepairService(
    AccessControlDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<WorkspaceOwnerAuthorityRepairService> logger)
{
    internal async Task RunAsync(string? targetWorkspaceId, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await AcquireOperatorLockAsync(transaction, cancellationToken);
            var workspaceIds = await dbContext.Roles
                .AsNoTracking()
                .Where(role => targetWorkspaceId == null || role.WorkspaceId == targetWorkspaceId)
                .Select(role => role.WorkspaceId)
                .Distinct()
                .OrderBy(workspaceId => workspaceId)
                .ToArrayAsync(cancellationToken);

            var repaired = 0;
            var alreadyCurrent = 0;
            var skippedCustomized = 0;
            var ambiguous = 0;

            foreach (var workspaceId in workspaceIds)
            {
                var candidates = await dbContext.Roles
                    .Where(role => role.WorkspaceId == workspaceId
                        && (role.NormalizedName == InitialWorkspaceAccessPolicy.RoleName.ToUpperInvariant()
                            || role.SourceTemplateId == InitialWorkspaceAccessPolicy.SystemOwnerTemplateId))
                    .OrderBy(role => role.RoleId)
                    .ToArrayAsync(cancellationToken);
                var ownerClaims = candidates
                    .Where(role => role.SourceTemplateId == InitialWorkspaceAccessPolicy.SystemOwnerTemplateId
                        || IsHistoricalSeedIdentity(role))
                    .ToArray();
                if (ownerClaims.Length > 1)
                {
                    ambiguous++;
                    throw new InvalidOperationException(
                        $"Workspace '{workspaceId}' has multiple roles claiming the system Workspace Owner identity; no authority was changed.");
                }

                if (ownerClaims.Length == 0)
                {
                    skippedCustomized += candidates.Length;
                    continue;
                }

                var role = ownerClaims[0];
                var capabilities = await dbContext.RoleCapabilities
                    .Where(item => item.RoleId == role.RoleId)
                    .Select(item => item.Capability)
                    .OrderBy(capability => capability)
                    .ToArrayAsync(cancellationToken);
                var currentCapabilities = InitialWorkspaceAccessPolicy.Validated();
                var exactCurrent = capabilities.SequenceEqual(currentCapabilities, StringComparer.Ordinal);
                if (role.SourceTemplateId == InitialWorkspaceAccessPolicy.SystemOwnerTemplateId)
                {
                    if (HasCurrentSystemIdentity(role) && exactCurrent)
                        alreadyCurrent++;
                    else
                        skippedCustomized++;
                    continue;
                }

                var assignmentExists = await dbContext.MembershipRoleAssignments
                    .AsNoTracking()
                    .AnyAsync(item => item.WorkspaceId == workspaceId && item.RoleId == role.RoleId, cancellationToken);
                var hasRestrictiveScope = await dbContext.RoleDataScopes
                    .AsNoTracking()
                    .AnyAsync(item => item.RoleId == role.RoleId && item.Scope != AccessDataScope.Workspace, cancellationToken);
                var hasFieldPolicy = await dbContext.RoleFieldSecurity
                    .AsNoTracking()
                    .AnyAsync(item => item.RoleId == role.RoleId, cancellationToken);
                if (!assignmentExists
                    || hasRestrictiveScope
                    || hasFieldPolicy
                    || (!exactCurrent
                        && !InitialWorkspaceAccessPolicy.IsKnownPreviousCapabilitySet(capabilities)))
                {
                    skippedCustomized++;
                    continue;
                }

                dbContext.RoleCapabilities.AddRange(currentCapabilities
                    .Except(capabilities, StringComparer.Ordinal)
                    .Select(capability => new RoleCapability(role.RoleId, capability)));
                role.MarkAsSystemOwned(InitialWorkspaceAccessPolicy.SystemOwnerTemplateId, timeProvider.GetUtcNow());
                await AdvanceDirectoryRevisionAsync(workspaceId, cancellationToken);
                repaired++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Workspace Owner authority repair completed: scanned={ScannedWorkspaces}, repaired={RepairedWorkspaces}, alreadyCurrent={AlreadyCurrentWorkspaces}, skippedCustomized={SkippedCustomizedRoles}, ambiguous={AmbiguousWorkspaces}.",
                workspaceIds.Length,
                repaired,
                alreadyCurrent,
                skippedCustomized,
                ambiguous);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool IsHistoricalSeedIdentity(AccessRole role) =>
        role.SourceTemplateId is null
        && InitialWorkspaceAccessPolicy.HasUntouchedSeedIdentity(role, role.WorkspaceId);

    private static bool HasCurrentSystemIdentity(AccessRole role) =>
        role.IsActive
        && string.Equals(role.Name, InitialWorkspaceAccessPolicy.RoleName, StringComparison.Ordinal)
        && string.Equals(role.Description, InitialWorkspaceAccessPolicy.RoleDescription, StringComparison.Ordinal);

    private async Task AdvanceDirectoryRevisionAsync(string workspaceId, CancellationToken cancellationToken)
    {
        var revision = await dbContext.WorkspaceDirectoryRevisions
            .FromSqlInterpolated($"SELECT [WorkspaceId], [Revision] FROM [access].[WorkspaceDirectoryRevisions] WITH (UPDLOCK, HOLDLOCK) WHERE [WorkspaceId] = {workspaceId}")
            .SingleOrDefaultAsync(cancellationToken);
        if (revision is null)
            dbContext.WorkspaceDirectoryRevisions.Add(new WorkspaceAccessDirectoryRevision(workspaceId));
        else
            revision.Advance();
    }

    private async Task AcquireOperatorLockAsync(
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = N'UnicoreCRM.AccessControl.WorkspaceOwnerAuthorityRepair',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 60000;
            SELECT @result;
            """;
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (result < 0)
            throw new InvalidOperationException($"Could not acquire the Workspace Owner authority repair lock (sp_getapplock result {result}).");
    }
}
