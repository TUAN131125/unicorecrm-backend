using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.AccessControl.Application.ReplaceWorkspaceMemberAccess;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

/// <summary>
/// The owner-local <c>MEMBER_ACCESS_REPLACEMENT_TRANSACTION</c>. The Workspace directory-revision
/// row provides the common AccessControl lock order, while the distinct member-access anchor is the
/// only value compared with <c>If-Match</c> and exposed as the command aggregate version.
/// </summary>
internal sealed class EfReplaceWorkspaceMemberAccessPersistence(
    AccessControlDbContext dbContext,
    TimeProvider timeProvider) : IReplaceWorkspaceMemberAccessPersistence
{
    private const string OperationId = "replaceWorkspaceMemberAccess";
    private const string AdministratorCapability = "access.configure";
    private const int DuplicateKey = 2601;
    private const int UniqueConstraint = 2627;
    private const int DeadlockVictim = 1205;

    public Task<MemberAccessCommandIdempotencyRecord?> FindIdempotencyAsync(
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
        return dbContext.MemberAccessCommandIdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);
    }

    public async Task<ReplaceWorkspaceMemberAccessCommitResult> TryCommitAsync(
        string workspaceId,
        string actorAccountId,
        string actorMembershipId,
        string actorMemberId,
        string requestId,
        string correlationId,
        string idempotencyKey,
        NormalizedReplaceWorkspaceMemberAccess request,
        IReadOnlySet<string> activeMembershipIds,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var scopeKey = AccessRoleCommandIdempotencyRecord.CreateScopeKey(
                OperationId,
                workspaceId,
                actorMembershipId,
                idempotencyKey);
            var existingCompletion = await dbContext.MemberAccessCommandIdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);
            if (existingCompletion is not null)
            {
                return string.Equals(existingCompletion.Fingerprint, request.Fingerprint, StringComparison.Ordinal)
                    ? new ReplaceWorkspaceMemberAccessCommitResult(
                        ReplaceWorkspaceMemberAccessCommitStatus.Replay,
                        Replay(existingCompletion))
                    : new ReplaceWorkspaceMemberAccessCommitResult(
                        ReplaceWorkspaceMemberAccessCommitStatus.IdempotencyKeyReused);
            }

            // Every AccessControl directory mutation takes this lock first. It serializes this
            // prospective administrator calculation with role create/replace/archive while its
            // value remains distinct from MemberAccessVersion.
            var revision = await LockRevisionAsync(workspaceId, cancellationToken);
            var versionAnchor = await LockMemberAccessVersionAsync(
                workspaceId,
                request.MembershipId,
                cancellationToken);
            var priorVersion = versionAnchor?.Version ?? 0;
            if (request.ExpectedMemberAccessVersion != priorVersion)
            {
                return new ReplaceWorkspaceMemberAccessCommitResult(
                    ReplaceWorkspaceMemberAccessCommitStatus.VersionConflict);
            }

            var submittedRoles = await dbContext.Roles
                .Where(item => item.WorkspaceId == workspaceId && request.RoleIds.Contains(item.RoleId))
                .ToListAsync(cancellationToken);
            if (submittedRoles.Count != request.RoleIds.Count)
            {
                return new ReplaceWorkspaceMemberAccessCommitResult(
                    ReplaceWorkspaceMemberAccessCommitStatus.RoleNotFound);
            }
            if (submittedRoles.Any(item => !item.IsActive))
            {
                return new ReplaceWorkspaceMemberAccessCommitResult(
                    ReplaceWorkspaceMemberAccessCommitStatus.RoleInactive);
            }

            if (!await ProspectiveAdministratorExistsAsync(
                    workspaceId,
                    request.MembershipId,
                    request.RoleIds,
                    activeMembershipIds,
                    cancellationToken))
            {
                return new ReplaceWorkspaceMemberAccessCommitResult(
                    ReplaceWorkspaceMemberAccessCommitStatus.LastWorkspaceAdministrator);
            }

            var existingAssignments = await dbContext.MembershipRoleAssignments
                .Where(item => item.WorkspaceId == workspaceId && item.MembershipId == request.MembershipId)
                .ToListAsync(cancellationToken);
            var wantedRoleIds = request.RoleIds.ToHashSet(StringComparer.Ordinal);
            var storedRoleIds = existingAssignments
                .Select(item => item.RoleId)
                .ToHashSet(StringComparer.Ordinal);
            dbContext.MembershipRoleAssignments.RemoveRange(
                existingAssignments.Where(item => !wantedRoleIds.Contains(item.RoleId)));

            var now = timeProvider.GetUtcNow();
            dbContext.MembershipRoleAssignments.AddRange(request.RoleIds
                .Where(roleId => !storedRoleIds.Contains(roleId))
                .Select(roleId => new MembershipRoleAssignment(
                    workspaceId,
                    request.MembershipId,
                    roleId,
                    now)));

            if (versionAnchor is null)
            {
                versionAnchor = new MemberAccessVersionAnchor(workspaceId, request.MembershipId);
                dbContext.MemberAccessVersions.Add(versionAnchor);
            }
            else
            {
                versionAnchor.Advance();
            }
            var resultingVersion = versionAnchor.Version;

            if (revision is null)
            {
                revision = new WorkspaceAccessDirectoryRevision(workspaceId);
                dbContext.WorkspaceDirectoryRevisions.Add(revision);
            }
            else
            {
                revision.Advance();
            }

            var commandId = AccessControlIds.New("command");
            var audit = AccessGovernanceCommandAudit.ForMemberAccess(
                OperationId,
                commandId,
                workspaceId,
                actorAccountId,
                actorMembershipId,
                actorMemberId,
                requestId,
                correlationId,
                request.MembershipId,
                priorVersion,
                resultingVersion,
                now);
            var outbox = AccessControlOutboxEvent.ForMemberAccess(
                "WORKSPACE_MEMBER_ACCESS_REPLACED",
                request.MembershipId,
                resultingVersion,
                correlationId,
                commandId,
                JsonSerializer.Serialize(new
                {
                    membershipId = request.MembershipId,
                    version = resultingVersion
                }),
                now,
                workspaceId);
            var completion = new MemberAccessCommandIdempotencyRecord(
                OperationId,
                workspaceId,
                actorMembershipId,
                idempotencyKey,
                request.Fingerprint,
                commandId,
                request.MembershipId,
                resultingVersion,
                audit.EvidenceId,
                outbox.EventId,
                revision.Revision,
                correlationId,
                now);

            dbContext.AccessGovernanceCommandAudits.Add(audit);
            dbContext.AccessControlOutboxEvents.Add(outbox);
            dbContext.MemberAccessCommandIdempotencyRecords.Add(completion);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ReplaceWorkspaceMemberAccessCommitResult(
                ReplaceWorkspaceMemberAccessCommitStatus.Committed,
                new ReplaceWorkspaceMemberAccessCommit(
                    commandId,
                    request.MembershipId,
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
            return new ReplaceWorkspaceMemberAccessCommitResult(
                ReplaceWorkspaceMemberAccessCommitStatus.Contention);
        }
        catch (SqlException exception) when (exception.Number == DeadlockVictim)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return new ReplaceWorkspaceMemberAccessCommitResult(
                ReplaceWorkspaceMemberAccessCommitStatus.Contention);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    private async Task<bool> ProspectiveAdministratorExistsAsync(
        string workspaceId,
        string targetMembershipId,
        IReadOnlyList<string> replacementRoleIds,
        IReadOnlySet<string> activeMembershipIds,
        CancellationToken cancellationToken)
    {
        if (activeMembershipIds.Count == 0)
            return false;

        var administratorRoleIds = await dbContext.Roles
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && item.IsActive)
            .Where(item => dbContext.RoleCapabilities.Any(
                capability => capability.RoleId == item.RoleId
                              && capability.Capability == AdministratorCapability))
            .Select(item => item.RoleId)
            .ToListAsync(cancellationToken);
        if (administratorRoleIds.Count == 0)
            return false;

        var targetRemainsAdministrator = activeMembershipIds.Contains(targetMembershipId)
            && replacementRoleIds.Any(roleId => administratorRoleIds.Contains(roleId, StringComparer.Ordinal));
        if (targetRemainsAdministrator)
            return true;

        return await dbContext.MembershipRoleAssignments
            .AsNoTracking()
            .AnyAsync(
                assignment => assignment.WorkspaceId == workspaceId
                              && assignment.MembershipId != targetMembershipId
                              && activeMembershipIds.Contains(assignment.MembershipId)
                              && administratorRoleIds.Contains(assignment.RoleId),
                cancellationToken);
    }

    private async Task<WorkspaceAccessDirectoryRevision?> LockRevisionAsync(
        string workspaceId,
        CancellationToken cancellationToken) =>
        await dbContext.WorkspaceDirectoryRevisions
            .FromSqlInterpolated($"SELECT [WorkspaceId], [Revision] FROM [access].[WorkspaceDirectoryRevisions] WITH (UPDLOCK, HOLDLOCK) WHERE [WorkspaceId] = {workspaceId}")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<MemberAccessVersionAnchor?> LockMemberAccessVersionAsync(
        string workspaceId,
        string membershipId,
        CancellationToken cancellationToken) =>
        await dbContext.MemberAccessVersions
            .FromSqlInterpolated($"SELECT [WorkspaceId], [MembershipId], [Version] FROM [access].[MemberAccessVersions] WITH (UPDLOCK, HOLDLOCK) WHERE [WorkspaceId] = {workspaceId} AND [MembershipId] = {membershipId}")
            .SingleOrDefaultAsync(cancellationToken);

    private static bool IsContention(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: DuplicateKey or UniqueConstraint or DeadlockVictim };

    private static ReplaceWorkspaceMemberAccessCommit Replay(MemberAccessCommandIdempotencyRecord record) => new(
        record.CommandId,
        record.MembershipId,
        record.MemberAccessVersion,
        record.AuditEvidenceId,
        record.EventId,
        record.DirectoryRevisionAtCommit,
        record.CorrelationId,
        record.OccurredAt,
        true);
}
