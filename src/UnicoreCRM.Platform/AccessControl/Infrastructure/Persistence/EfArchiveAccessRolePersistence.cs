using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.AccessControl.Application.ArchiveAccessRole;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

/// <summary>
/// The owner-local <c>ROLE_ARCHIVE_TRANSACTION</c>. One serializable AccessControl transaction
/// atomically persists the deactivation, the updated timestamp, the role version increment, exactly
/// one Workspace directory-revision increment, the idempotency completion, the governance audit
/// carrying the normalized reason, and the <c>ACCESS_ROLE_ARCHIVED</c> outbox row.
///
/// <para>Archive is lifecycle-only. No capability, data-scope, field-security or assignment row is
/// read for mutation or written, no foreign owner participates, and any required failure rolls back
/// all seven effects.</para>
/// </summary>
internal sealed class EfArchiveAccessRolePersistence(
    AccessControlDbContext dbContext,
    TimeProvider timeProvider) : IArchiveAccessRolePersistence
{
    private const string OperationId = "archiveAccessRole";
    private const string AdministratorCapability = "access.configure";
    private const int DuplicateKey = 2601;
    private const int UniqueConstraint = 2627;
    private const int DeadlockVictim = 1205;

    public Task<AccessRoleCommandIdempotencyRecord?> FindIdempotencyAsync(
        string workspaceId,
        string actorMembershipId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var scopeKey = AccessRoleCommandIdempotencyRecord.CreateScopeKey(
            OperationId,
            workspaceId,
            actorMembershipId,
            idempotencyKey);
        return dbContext.AccessRoleCommandIdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);
    }

    public async Task<ArchiveAccessRoleCommitResult> TryCommitAsync(
        string workspaceId,
        string actorAccountId,
        string actorMembershipId,
        string actorMemberId,
        string requestId,
        string correlationId,
        string idempotencyKey,
        NormalizedArchiveAccessRole request,
        IReadOnlySet<string>? activeMembershipIds,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var scopeKey = AccessRoleCommandIdempotencyRecord.CreateScopeKey(
                OperationId,
                workspaceId,
                actorMembershipId,
                idempotencyKey);
            var existing = await dbContext.AccessRoleCommandIdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);
            if (existing is not null)
            {
                return string.Equals(existing.Fingerprint, request.Fingerprint, StringComparison.Ordinal)
                    ? new ArchiveAccessRoleCommitResult(ArchiveAccessRoleCommitStatus.Replay, Replay(existing))
                    : new ArchiveAccessRoleCommitResult(ArchiveAccessRoleCommitStatus.IdempotencyKeyReused);
            }

            var revision = await LockRevisionAsync(workspaceId, cancellationToken);

            // The target is row-locked so the version comparison is the authority rather than a
            // read-then-write window: two concurrent archives carrying the same expected version
            // resolve to exactly one commit and one VERSION_CONFLICT with zero mutation.
            var role = await LockRoleAsync(workspaceId, request.RoleId, cancellationToken);
            if (role is null)
                return new ArchiveAccessRoleCommitResult(ArchiveAccessRoleCommitStatus.RoleNotFound);
            if (!role.IsActive)
                return new ArchiveAccessRoleCommitResult(ArchiveAccessRoleCommitStatus.RoleInactive);
            if (role.Version != request.ExpectedVersion)
                return new ArchiveAccessRoleCommitResult(ArchiveAccessRoleCommitStatus.VersionConflict);

            var guard = await EvaluateAdministratorGuardAsync(workspaceId, role, activeMembershipIds, cancellationToken);
            if (guard is not null)
                return new ArchiveAccessRoleCommitResult(guard.Value);

            var now = timeProvider.GetUtcNow();
            var commandId = AccessControlIds.New("command");
            var priorVersion = role.Version;

            role.Archive(now);
            var resultingVersion = role.Version;

            var audit = new AccessGovernanceCommandAudit(
                OperationId,
                commandId,
                workspaceId,
                actorAccountId,
                actorMembershipId,
                actorMemberId,
                requestId,
                correlationId,
                role.RoleId,
                priorVersion,
                resultingVersion,
                request.Reason,
                now);
            // The reason is deliberately absent from the event payload: it is governance provenance
            // for the audit trail, not a fact consumers of the archive event are given.
            var outbox = new AccessControlOutboxEvent(
                "ACCESS_ROLE_ARCHIVED",
                role.RoleId,
                resultingVersion,
                correlationId,
                commandId,
                JsonSerializer.Serialize(new { roleId = role.RoleId, version = resultingVersion }),
                now,
                workspaceId);

            if (revision is null)
            {
                revision = new WorkspaceAccessDirectoryRevision(workspaceId);
                dbContext.WorkspaceDirectoryRevisions.Add(revision);
            }
            else
            {
                revision.Advance();
            }

            var idempotency = new AccessRoleCommandIdempotencyRecord(
                OperationId,
                workspaceId,
                actorMembershipId,
                idempotencyKey,
                request.Fingerprint,
                commandId,
                role.RoleId,
                resultingVersion,
                audit.EvidenceId,
                outbox.EventId,
                revision.Revision,
                correlationId,
                now);
            dbContext.AccessGovernanceCommandAudits.Add(audit);
            dbContext.AccessControlOutboxEvents.Add(outbox);
            dbContext.AccessRoleCommandIdempotencyRecords.Add(idempotency);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ArchiveAccessRoleCommitResult(
                ArchiveAccessRoleCommitStatus.Committed,
                new ArchiveAccessRoleCommit(
                    commandId,
                    role.RoleId,
                    resultingVersion,
                    audit.EvidenceId,
                    outbox.EventId,
                    revision.Revision,
                    correlationId,
                    now,
                    false));
        }
        catch (DbUpdateException exception) when (IsContention(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return new ArchiveAccessRoleCommitResult(ArchiveAccessRoleCommitStatus.Contention);
        }
        catch (SqlException exception) when (exception.Number == DeadlockVictim)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return new ArchiveAccessRoleCommitResult(ArchiveAccessRoleCommitStatus.Contention);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// The last-Workspace-administrator guard. Archiving removes the target from the active
    /// administrator set, because an inactive role contributes no effective authority, so the guard
    /// engages whenever the target currently is an administrator role: active, holding
    /// <c>access.configure</c>, and assigned to at least one authoritative active membership.
    ///
    /// <para>Membership activity is a Workspace fact, so when the target is administrative but that
    /// fact has not been supplied the transaction rolls back unwritten and the caller re-enters with
    /// the read-only snapshot. Archiving a role that does not hold <c>access.configure</c> never
    /// reaches the provider at all. Returning null means the guard did not reject the archive.</para>
    /// </summary>
    private async Task<ArchiveAccessRoleCommitStatus?> EvaluateAdministratorGuardAsync(
        string workspaceId,
        AccessRole role,
        IReadOnlySet<string>? activeMembershipIds,
        CancellationToken cancellationToken)
    {
        var targetHoldsAdministratorCapability = await dbContext.RoleCapabilities.AsNoTracking()
            .AnyAsync(item => item.RoleId == role.RoleId && item.Capability == AdministratorCapability, cancellationToken);
        if (!targetHoldsAdministratorCapability)
            return null;

        if (activeMembershipIds is null)
            return ArchiveAccessRoleCommitStatus.ProviderFactsRequired;

        var administratorRoleIds = await dbContext.Roles.AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && item.IsActive)
            .Where(item => dbContext.RoleCapabilities.Any(
                capability => capability.RoleId == item.RoleId && capability.Capability == AdministratorCapability))
            .Where(item => dbContext.MembershipRoleAssignments.Any(
                assignment => assignment.RoleId == item.RoleId
                              && assignment.WorkspaceId == workspaceId
                              && activeMembershipIds.Contains(assignment.MembershipId)))
            .Select(item => item.RoleId)
            .ToListAsync(cancellationToken);

        var targetIsAdministrator = administratorRoleIds.Contains(role.RoleId, StringComparer.Ordinal);
        return targetIsAdministrator && administratorRoleIds.Count == 1
            ? ArchiveAccessRoleCommitStatus.LastWorkspaceAdministrator
            : null;
    }

    private async Task<WorkspaceAccessDirectoryRevision?> LockRevisionAsync(
        string workspaceId,
        CancellationToken cancellationToken) =>
        await dbContext.WorkspaceDirectoryRevisions
            .FromSqlInterpolated($"SELECT [WorkspaceId], [Revision] FROM [access].[WorkspaceDirectoryRevisions] WITH (UPDLOCK, HOLDLOCK) WHERE [WorkspaceId] = {workspaceId}")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<AccessRole?> LockRoleAsync(
        string workspaceId,
        string roleId,
        CancellationToken cancellationToken) =>
        await dbContext.Roles
            .FromSqlInterpolated($"SELECT [RoleId], [WorkspaceId], [Name], [NormalizedName], [Description], [SourceTemplateId], [IsActive], [Version], [CreatedAt], [UpdatedAt] FROM [access].[Roles] WITH (UPDLOCK, HOLDLOCK) WHERE [RoleId] = {roleId} AND [WorkspaceId] = {workspaceId}")
            .SingleOrDefaultAsync(cancellationToken);

    private static bool IsContention(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: DuplicateKey or UniqueConstraint or DeadlockVictim };

    private static ArchiveAccessRoleCommit Replay(AccessRoleCommandIdempotencyRecord record) => new(
        record.CommandId,
        record.RoleId,
        record.RoleVersion,
        record.AuditEvidenceId,
        record.EventId,
        record.DirectoryRevisionAtCommit,
        record.CorrelationId,
        record.OccurredAt,
        true);
}
