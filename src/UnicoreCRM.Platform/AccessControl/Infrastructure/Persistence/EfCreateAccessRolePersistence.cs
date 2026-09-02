using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.AccessControl.Application.CreateAccessRole;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

internal sealed class EfCreateAccessRolePersistence(
    AccessControlDbContext dbContext,
    TimeProvider timeProvider) : ICreateAccessRolePersistence
{
    private const string OperationId = "createAccessRole";
    private const int DuplicateKey = 2601;
    private const int UniqueConstraint = 2627;
    private const int DeadlockVictim = 1205;

    public Task<AccessRoleCommandIdempotencyRecord?> FindIdempotencyAsync(
        string workspaceId,
        string actorMembershipId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var scopeKey = AccessRoleCommandIdempotencyRecord.CreateScopeKey(OperationId, workspaceId, actorMembershipId, idempotencyKey);
        return dbContext.AccessRoleCommandIdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);
    }

    public async Task<CreateAccessRoleCommitResult> TryCommitAsync(
        string workspaceId,
        string actorAccountId,
        string actorMembershipId,
        string actorMemberId,
        string requestId,
        string correlationId,
        string idempotencyKey,
        NormalizedCreateAccessRole request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var scopeKey = AccessRoleCommandIdempotencyRecord.CreateScopeKey(OperationId, workspaceId, actorMembershipId, idempotencyKey);
            var existing = await dbContext.AccessRoleCommandIdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);
            if (existing is not null)
            {
                return string.Equals(existing.Fingerprint, request.Fingerprint, StringComparison.Ordinal)
                    ? new CreateAccessRoleCommitResult(CreateAccessRoleCommitStatus.Replay, Replay(existing))
                    : new CreateAccessRoleCommitResult(CreateAccessRoleCommitStatus.IdempotencyKeyReused);
            }

            var revision = await LockRevisionAsync(workspaceId, cancellationToken);
            if (await dbContext.Roles.AsNoTracking().AnyAsync(
                    item => item.WorkspaceId == workspaceId && item.NormalizedName == request.NormalizedName,
                    cancellationToken))
            {
                return new CreateAccessRoleCommitResult(CreateAccessRoleCommitStatus.RoleNameConflict);
            }

            var roleCount = await dbContext.Roles.AsNoTracking().CountAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
            var scopeCount = await dbContext.RoleDataScopes.AsNoTracking().CountAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
            var fieldCount = await dbContext.RoleFieldSecurity.AsNoTracking().CountAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
            if (roleCount >= 500 || scopeCount + request.DataScopes.Count > 5000 || fieldCount + request.FieldSecurity.Count > 10000)
                return new CreateAccessRoleCommitResult(CreateAccessRoleCommitStatus.LifecycleConflict);

            var now = timeProvider.GetUtcNow();
            var commandId = AccessControlIds.New("command");
            var role = new AccessRole(workspaceId, request.Name, request.Description, request.SourceTemplateId, now);
            if (!string.Equals(role.NormalizedName, request.NormalizedName, StringComparison.Ordinal))
                throw new InvalidOperationException("Role normalization drifted from the frozen request normalization.");
            var capabilities = request.Capabilities.Select(item => new RoleCapability(role.RoleId, item)).ToArray();
            var scopes = request.DataScopes.Select(item => new RoleDataScopePolicy(
                workspaceId,
                role.RoleId,
                item.ResourceKey,
                item.Scope,
                JsonSerializer.Serialize(item.AllowedOwnerIds))).ToArray();
            var fields = request.FieldSecurity.Select(item => new RoleFieldSecurityPolicy(
                workspaceId,
                role.RoleId,
                item.ResourceKey,
                item.FieldKey,
                item.Access)).ToArray();
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
                null,
                0,
                null,
                now);
            var outbox = new AccessControlOutboxEvent(
                "ACCESS_ROLE_CREATED",
                role.RoleId,
                0,
                correlationId,
                commandId,
                JsonSerializer.Serialize(new { roleId = role.RoleId, version = 0 }),
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
                0,
                audit.EvidenceId,
                outbox.EventId,
                revision.Revision,
                correlationId,
                now);
            dbContext.Roles.Add(role);
            dbContext.RoleCapabilities.AddRange(capabilities);
            dbContext.RoleDataScopes.AddRange(scopes);
            dbContext.RoleFieldSecurity.AddRange(fields);
            dbContext.AccessGovernanceCommandAudits.Add(audit);
            dbContext.AccessControlOutboxEvents.Add(outbox);
            dbContext.AccessRoleCommandIdempotencyRecords.Add(idempotency);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CreateAccessRoleCommitResult(
                CreateAccessRoleCommitStatus.Committed,
                new CreateAccessRoleCommit(
                    commandId,
                    role.RoleId,
                    0,
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
            return new CreateAccessRoleCommitResult(CreateAccessRoleCommitStatus.Contention);
        }
        catch (SqlException exception) when (exception.Number == DeadlockVictim)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return new CreateAccessRoleCommitResult(CreateAccessRoleCommitStatus.Contention);
        }
    }

    private async Task<WorkspaceAccessDirectoryRevision?> LockRevisionAsync(
        string workspaceId,
        CancellationToken cancellationToken) =>
        await dbContext.WorkspaceDirectoryRevisions
            .FromSqlInterpolated($"SELECT [WorkspaceId], [Revision] FROM [access].[WorkspaceDirectoryRevisions] WITH (UPDLOCK, HOLDLOCK) WHERE [WorkspaceId] = {workspaceId}")
            .SingleOrDefaultAsync(cancellationToken);

    private static bool IsContention(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: DuplicateKey or UniqueConstraint or DeadlockVictim };

    private static CreateAccessRoleCommit Replay(AccessRoleCommandIdempotencyRecord record) => new(
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
