using UnicoreCRM.Platform.AccessControl.Application.AccessDirectory;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.ArchiveAccessRole;

/// <summary>
/// <c>POST /access/roles/{roleId}/archive</c> under <c>DEC-ARCHIVEACCESSROLE-AUTHORITY-CLOSURE</c>.
///
/// <para>The stage order is frozen and total: authentication and the Trusted Workspace are
/// established by the pipeline, then <c>access.configure</c>, then required metadata and
/// <c>If-Match</c> syntax, then <c>reason</c> validation, then idempotency, and only then the target
/// role. Nothing before the idempotency stage reads the target, so a caller who fails authorization,
/// metadata or request validation learns nothing about whether the role exists, its active state,
/// its version, its assignments or the Workspace administrator population.</para>
///
/// <para>Resolving idempotency before the lifecycle guard is what makes retrying a successful
/// archive return the original committed result rather than <c>409 ROLE_INACTIVE</c> against the
/// role it just archived.</para>
/// </summary>
internal sealed class Handler(
    IAccessContextAuthorizer authorizer,
    ICurrentWorkspace currentWorkspace,
    IArchiveAccessRolePersistence persistence,
    IWorkspaceAccessDirectorySource workspaceSource,
    DirectoryComposer composer)
{
    private const int ContentionAttempts = 5;

    internal async Task<AccessOperationResult<AccessMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var authorization = await authorizer.AuthorizeWithContextAsync(
            AccessCapabilities.AccessConfigure,
            command.CorrelationId,
            cancellationToken);
        if (!authorization.IsAllowed)
        {
            return AccessOperationResult<AccessMutationResponse>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH" ? AccessErrors.WorkspaceMismatch() : AccessErrors.AccessDenied());
        }

        var metadataErrors = AccessRoleCommandMetadata.Validate(
            command.RequestId,
            command.SuppliedCorrelationId,
            command.IdempotencyKey,
            command.IfMatch,
            out var expectedVersion);
        if (metadataErrors.Count != 0)
            return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.Validation(metadataErrors));
        if (command.Body.ExceededLimit)
            return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.PayloadTooLarge());
        if (!ArchiveAccessRoleNormalizer.TryNormalize(
                command.RoleId,
                expectedVersion,
                command.Body.Value,
                out var request,
                out var requestErrors))
        {
            return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.Validation(requestErrors));
        }

        var trusted = currentWorkspace.Require();
        var existing = await persistence.FindIdempotencyAsync(
            trusted.WorkspaceId,
            trusted.MembershipId,
            command.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.Fingerprint, request!.Fingerprint, StringComparison.Ordinal))
                return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.IdempotencyKeyReused(command.IdempotencyKey));
            return await ComposeAsync(Replay(existing), cancellationToken);
        }

        IReadOnlySet<string>? activeMembershipIds = null;
        for (var attempt = 0; attempt < ContentionAttempts; attempt++)
        {
            var result = await persistence.TryCommitAsync(
                trusted.WorkspaceId,
                trusted.AccountId,
                trusted.MembershipId,
                trusted.MemberId,
                command.RequestId,
                command.CorrelationId,
                command.IdempotencyKey,
                request!,
                activeMembershipIds,
                cancellationToken);
            switch (result.Status)
            {
                case ArchiveAccessRoleCommitStatus.Committed:
                case ArchiveAccessRoleCommitStatus.Replay:
                    return await ComposeAsync(result.Commit!, cancellationToken);
                case ArchiveAccessRoleCommitStatus.IdempotencyKeyReused:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.IdempotencyKeyReused(command.IdempotencyKey));
                case ArchiveAccessRoleCommitStatus.RoleNotFound:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.ResourceNotFound());
                case ArchiveAccessRoleCommitStatus.RoleInactive:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.RoleInactive());
                case ArchiveAccessRoleCommitStatus.VersionConflict:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.VersionConflict());
                case ArchiveAccessRoleCommitStatus.LastWorkspaceAdministrator:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.LastWorkspaceAdministrator());
                case ArchiveAccessRoleCommitStatus.ProviderFactsRequired:
                    // The transaction reached the last-administrator guard and rolled back without
                    // writing, so the Workspace membership snapshot is read outside the owner
                    // transaction and only when the target actually is an administrator role.
                    // Reading it here rather than up front keeps 404, ROLE_INACTIVE and 412 ahead of
                    // any foreign-provider dependency, and leaves an ordinary archive owner-local.
                    activeMembershipIds = await ReadActiveMembershipsAsync(trusted.WorkspaceId, cancellationToken);
                    if (activeMembershipIds is null)
                        return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.IntegrationUnavailable());
                    continue;
                case ArchiveAccessRoleCommitStatus.Contention:
                    continue;
                default:
                    throw new InvalidOperationException("Unsupported archiveAccessRole persistence result.");
            }
        }
        throw new InvalidOperationException("archiveAccessRole did not converge after database contention.");
    }

    /// <summary>
    /// The read-only Workspace fact the last-administrator guard needs. Only the literal
    /// <c>active</c> membership status counts. Provider unavailability or an invalid snapshot
    /// returns null and fails the command closed with 503 before any mutation: it is never converted
    /// into an empty set, which would silently assert that no other administrator exists.
    /// </summary>
    private async Task<IReadOnlySet<string>?> ReadActiveMembershipsAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        WorkspaceAccessDirectorySnapshot? snapshot;
        try
        {
            snapshot = await workspaceSource.ReadAsync(workspaceId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }

        if (snapshot is null
            || !string.Equals(snapshot.WorkspaceId, workspaceId, StringComparison.Ordinal)
            || snapshot.Memberships.Any(item => string.IsNullOrWhiteSpace(item.MembershipId))
            || snapshot.Memberships.GroupBy(item => item.MembershipId, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            return null;
        }

        return snapshot.Memberships
            .Where(item => string.Equals(item.Status, "active", StringComparison.Ordinal))
            .Select(item => item.MembershipId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<AccessOperationResult<AccessMutationResponse>> ComposeAsync(
        ArchiveAccessRoleCommit commit,
        CancellationToken cancellationToken)
    {
        var directory = await composer.ComposeAsync(currentWorkspace.Require().WorkspaceId, cancellationToken);
        if (!directory.IsSuccess)
            return AccessOperationResult<AccessMutationResponse>.Failure(directory.Error!);
        return AccessOperationResult<AccessMutationResponse>.Success(new AccessMutationResponse(
            commit.CommandId,
            commit.CorrelationId,
            commit.RoleId,
            "ACCESS_ROLE",
            commit.RoleVersion,
            commit.OccurredAt,
            commit.IsReplay ? "REPLAYED" : "COMMITTED",
            directory.Value!,
            [],
            [commit.EventId],
            [commit.AuditEvidenceId]));
    }

    private static ArchiveAccessRoleCommit Replay(Domain.AccessRoleCommandIdempotencyRecord record) => new(
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
