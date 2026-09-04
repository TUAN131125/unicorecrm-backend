using UnicoreCRM.Platform.AccessControl.Application.AccessDirectory;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.ReplaceAccessRole;

/// <summary>
/// <c>PUT /access/roles/{roleId}</c> under <c>DEC-REPLACEACCESSROLE-AUTHORITY-CLOSURE</c>.
///
/// <para>The stage order is frozen and total: authentication and the Trusted Workspace are
/// established by the pipeline, then <c>access.configure</c>, then required metadata and
/// <c>If-Match</c> syntax, then normalized request validation, then idempotency, and only then the
/// target role. Nothing before the idempotency stage reads the target, so a caller who fails
/// authorization, metadata or request validation learns nothing about whether the role exists, its
/// version, its configuration, its name-conflict status or the Workspace administrator population.
/// </para>
/// </summary>
internal sealed class Handler(
    IAccessContextAuthorizer authorizer,
    ICurrentWorkspace currentWorkspace,
    IReplaceAccessRolePersistence persistence,
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
        if (!ReplaceAccessRoleNormalizer.TryNormalize(
                command.RoleId,
                expectedVersion,
                command.Body.Value,
                out var request,
                out var requestErrors))
        {
            return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.Validation(requestErrors));
        }

        var capabilityErrors = request!.CapabilityInputs
            .Where(item => !AssignableCapabilityCatalog.Contains(item.Value))
            .ToDictionary(
                item => $"capabilities[{item.OriginalIndex}]",
                _ => new[] { "Capability is not assignable to a custom Workspace role." },
                StringComparer.Ordinal);
        if (capabilityErrors.Count != 0)
            return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.Validation(capabilityErrors));

        var trusted = currentWorkspace.Require();
        var existing = await persistence.FindIdempotencyAsync(
            trusted.WorkspaceId,
            trusted.MembershipId,
            command.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.Fingerprint, request.Fingerprint, StringComparison.Ordinal))
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
                request,
                activeMembershipIds,
                cancellationToken);
            switch (result.Status)
            {
                case ReplaceAccessRoleCommitStatus.Committed:
                case ReplaceAccessRoleCommitStatus.Replay:
                    return await ComposeAsync(result.Commit!, cancellationToken);
                case ReplaceAccessRoleCommitStatus.IdempotencyKeyReused:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.IdempotencyKeyReused(command.IdempotencyKey));
                case ReplaceAccessRoleCommitStatus.RoleNotFound:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.ResourceNotFound());
                case ReplaceAccessRoleCommitStatus.RoleInactive:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.RoleInactive());
                case ReplaceAccessRoleCommitStatus.VersionConflict:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.VersionConflict());
                case ReplaceAccessRoleCommitStatus.RoleNameConflict:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.RoleNameConflict());
                case ReplaceAccessRoleCommitStatus.LastWorkspaceAdministrator:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.LastWorkspaceAdministrator());
                case ReplaceAccessRoleCommitStatus.LifecycleConflict:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.LifecycleConflict());
                case ReplaceAccessRoleCommitStatus.ProviderFactsRequired:
                    // The transaction reached the last-administrator guard and rolled back without
                    // writing, so the Workspace membership snapshot is read outside the owner
                    // transaction and only for a replacement that actually removes
                    // access.configure from a currently administrative role. Reading it here rather
                    // than up front keeps 404, ROLE_INACTIVE, 412 and ROLE_NAME_CONFLICT ahead of
                    // any foreign-provider dependency.
                    activeMembershipIds = await ReadActiveMembershipsAsync(trusted.WorkspaceId, cancellationToken);
                    if (activeMembershipIds is null)
                        return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.IntegrationUnavailable());
                    continue;
                case ReplaceAccessRoleCommitStatus.Contention:
                    continue;
                default:
                    throw new InvalidOperationException("Unsupported replaceAccessRole persistence result.");
            }
        }
        throw new InvalidOperationException("replaceAccessRole did not converge after database contention.");
    }

    /// <summary>
    /// The read-only Workspace fact the last-administrator guard needs. Only the literal <c>active</c>
    /// membership status counts. Provider unavailability or an invalid snapshot returns null and
    /// fails the command closed with 503 before any mutation: it is never converted into an empty
    /// set, which would silently assert that no other administrator exists.
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
        ReplaceAccessRoleCommit commit,
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

    private static ReplaceAccessRoleCommit Replay(Domain.AccessRoleCommandIdempotencyRecord record) => new(
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
