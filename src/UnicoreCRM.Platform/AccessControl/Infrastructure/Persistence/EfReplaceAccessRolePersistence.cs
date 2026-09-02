using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Application.ReplaceAccessRole;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

/// <summary>
/// The owner-local <c>ROLE_REPLACEMENT_TRANSACTION</c>. One serializable AccessControl transaction
/// atomically persists the role scalar replacement, the three replaced collections, the role
/// version increment, exactly one Workspace directory-revision increment, the idempotency
/// completion, the governance audit and the <c>ACCESS_ROLE_REPLACED</c> outbox row. No assignment
/// is touched, no foreign owner participates and any required failure rolls back all nine effects.
/// </summary>
internal sealed class EfReplaceAccessRolePersistence(
    AccessControlDbContext dbContext,
    TimeProvider timeProvider) : IReplaceAccessRolePersistence
{
    private const string OperationId = "replaceAccessRole";
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

    public async Task<ReplaceAccessRoleCommitResult> TryCommitAsync(
        string workspaceId,
        string actorAccountId,
        string actorMembershipId,
        string actorMemberId,
        string requestId,
        string correlationId,
        string idempotencyKey,
        NormalizedReplaceAccessRole request,
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
                    ? new ReplaceAccessRoleCommitResult(ReplaceAccessRoleCommitStatus.Replay, Replay(existing))
                    : new ReplaceAccessRoleCommitResult(ReplaceAccessRoleCommitStatus.IdempotencyKeyReused);
            }

            var revision = await LockRevisionAsync(workspaceId, cancellationToken);

            // The target is row-locked so the version comparison is the authority rather than a
            // read-then-write window: two concurrent replacements carrying the same expected
            // version resolve to exactly one commit and one VERSION_CONFLICT with zero mutation.
            var role = await LockRoleAsync(workspaceId, request.RoleId, cancellationToken);
            if (role is null)
                return new ReplaceAccessRoleCommitResult(ReplaceAccessRoleCommitStatus.RoleNotFound);
            if (!role.IsActive)
                return new ReplaceAccessRoleCommitResult(ReplaceAccessRoleCommitStatus.RoleInactive);
            if (role.Version != request.ExpectedVersion)
                return new ReplaceAccessRoleCommitResult(ReplaceAccessRoleCommitStatus.VersionConflict);

            // The role may keep its own normalized name; only a collision with another role in the
            // same Workspace is a conflict. The unique index remains the race-safe authority.
            if (await dbContext.Roles.AsNoTracking().AnyAsync(
                    item => item.WorkspaceId == workspaceId
                            && item.NormalizedName == request.NormalizedName
                            && item.RoleId != role.RoleId,
                    cancellationToken))
            {
                return new ReplaceAccessRoleCommitResult(ReplaceAccessRoleCommitStatus.RoleNameConflict);
            }

            var existingCapabilities = await dbContext.RoleCapabilities
                .Where(item => item.RoleId == role.RoleId)
                .ToListAsync(cancellationToken);
            var guard = await EvaluateAdministratorGuardAsync(
                workspaceId,
                role,
                existingCapabilities,
                request,
                activeMembershipIds,
                cancellationToken);
            if (guard is not null)
                return new ReplaceAccessRoleCommitResult(guard.Value);

            var existingScopes = await dbContext.RoleDataScopes
                .Where(item => item.RoleId == role.RoleId)
                .ToListAsync(cancellationToken);
            var existingFields = await dbContext.RoleFieldSecurity
                .Where(item => item.RoleId == role.RoleId)
                .ToListAsync(cancellationToken);

            // Capacity is prospective: the replaced role's current rows leave the Workspace total
            // before the replacement rows join it. The role count cannot change here.
            var workspaceScopes = await dbContext.RoleDataScopes.AsNoTracking()
                .CountAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
            var workspaceFields = await dbContext.RoleFieldSecurity.AsNoTracking()
                .CountAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
            if (workspaceScopes - existingScopes.Count + request.DataScopes.Count > 5000
                || workspaceFields - existingFields.Count + request.FieldSecurity.Count > 10000)
            {
                return new ReplaceAccessRoleCommitResult(ReplaceAccessRoleCommitStatus.LifecycleConflict);
            }

            var now = timeProvider.GetUtcNow();
            var commandId = AccessControlIds.New("command");
            var priorVersion = role.Version;

            role.Replace(request.Name, request.Description, request.SourceTemplateId, now);
            if (!string.Equals(role.NormalizedName, request.NormalizedName, StringComparison.Ordinal))
                throw new InvalidOperationException("Role normalization drifted from the frozen request normalization.");
            var resultingVersion = role.Version;

            ReplaceCapabilities(role.RoleId, existingCapabilities, request.Capabilities);
            ReplaceDataScopes(workspaceId, role.RoleId, existingScopes, request.DataScopes);
            ReplaceFieldSecurity(workspaceId, role.RoleId, existingFields, request.FieldSecurity);

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
                null,
                now);
            var outbox = new AccessControlOutboxEvent(
                "ACCESS_ROLE_REPLACED",
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
            return new ReplaceAccessRoleCommitResult(
                ReplaceAccessRoleCommitStatus.Committed,
                new ReplaceAccessRoleCommit(
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
            return new ReplaceAccessRoleCommitResult(ReplaceAccessRoleCommitStatus.Contention);
        }
        catch (SqlException exception) when (exception.Number == DeadlockVictim)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return new ReplaceAccessRoleCommitResult(ReplaceAccessRoleCommitStatus.Contention);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// The last-Workspace-administrator guard. It engages only when the replacement removes
    /// <c>access.configure</c> from a role that currently carries it: because replaceAccessRole
    /// changes neither the active state nor any assignment, capability removal is the only way it
    /// can make a role non-administrative.
    ///
    /// <para>An administrator role is active, currently holds <c>access.configure</c> and is
    /// assigned to at least one authoritative active Workspace membership. Membership activity is a
    /// Workspace fact, so when the guard engages without it the transaction rolls back unwritten
    /// and the caller re-enters with the read-only snapshot. Returning null means the guard did not
    /// reject the replacement.</para>
    /// </summary>
    private async Task<ReplaceAccessRoleCommitStatus?> EvaluateAdministratorGuardAsync(
        string workspaceId,
        AccessRole role,
        IReadOnlyList<RoleCapability> existingCapabilities,
        NormalizedReplaceAccessRole request,
        IReadOnlySet<string>? activeMembershipIds,
        CancellationToken cancellationToken)
    {
        var removesAdministratorCapability =
            existingCapabilities.Any(item => string.Equals(item.Capability, AdministratorCapability, StringComparison.Ordinal))
            && !request.Capabilities.Contains(AdministratorCapability, StringComparer.Ordinal);
        if (!removesAdministratorCapability)
            return null;

        if (activeMembershipIds is null)
            return ReplaceAccessRoleCommitStatus.ProviderFactsRequired;

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
            ? ReplaceAccessRoleCommitStatus.LastWorkspaceAdministrator
            : null;
    }

    private void ReplaceCapabilities(
        string roleId,
        IReadOnlyList<RoleCapability> existing,
        IReadOnlyList<string> replacement)
    {
        var wanted = new HashSet<string>(replacement, StringComparer.Ordinal);
        var stored = new HashSet<string>(existing.Select(item => item.Capability), StringComparer.Ordinal);
        dbContext.RoleCapabilities.RemoveRange(existing.Where(item => !wanted.Contains(item.Capability)));
        dbContext.RoleCapabilities.AddRange(replacement
            .Where(capability => !stored.Contains(capability))
            .Select(capability => new RoleCapability(roleId, capability)));
    }

    /// <summary>
    /// Full replacement with stable identity: an unchanged canonical <c>(RoleId, ResourceKey)</c>
    /// keeps its <c>PolicyId</c> even when its value changed, a key absent from the replacement is
    /// deleted and a newly introduced key receives a fresh owner-generated ID. Deleting and
    /// recreating every row would satisfy the same observable configuration while needlessly
    /// churning identities.
    /// </summary>
    private void ReplaceDataScopes(
        string workspaceId,
        string roleId,
        IReadOnlyList<RoleDataScopePolicy> existing,
        IReadOnlyList<NormalizedDataScope> replacement)
    {
        var byKey = existing.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        foreach (var wanted in replacement)
        {
            var ownerIdsJson = JsonSerializer.Serialize(wanted.AllowedOwnerIds);
            if (byKey.Remove(wanted.ResourceKey, out var stored))
                stored.Replace(wanted.Scope, ownerIdsJson);
            else
                dbContext.RoleDataScopes.Add(new RoleDataScopePolicy(workspaceId, roleId, wanted.ResourceKey, wanted.Scope, ownerIdsJson));
        }
        dbContext.RoleDataScopes.RemoveRange(byKey.Values);
    }

    private void ReplaceFieldSecurity(
        string workspaceId,
        string roleId,
        IReadOnlyList<RoleFieldSecurityPolicy> existing,
        IReadOnlyList<NormalizedFieldSecurity> replacement)
    {
        var byKey = existing.ToDictionary(item => (item.ResourceKey, item.FieldKey));
        foreach (var wanted in replacement)
        {
            if (byKey.Remove((wanted.ResourceKey, wanted.FieldKey), out var stored))
                stored.Replace(wanted.Access);
            else
                dbContext.RoleFieldSecurity.Add(new RoleFieldSecurityPolicy(workspaceId, roleId, wanted.ResourceKey, wanted.FieldKey, wanted.Access));
        }
        dbContext.RoleFieldSecurity.RemoveRange(byKey.Values);
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

    private static ReplaceAccessRoleCommit Replay(AccessRoleCommandIdempotencyRecord record) => new(
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
