using UnicoreCRM.Platform.AccessControl.Application.AccessDirectory;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.CreateAccessRole;

internal sealed class Handler(
    IAccessContextAuthorizer authorizer,
    ICurrentWorkspace currentWorkspace,
    ICreateAccessRolePersistence persistence,
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

        var metadataErrors = MetadataErrors(command);
        if (metadataErrors.Count != 0)
            return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.Validation(metadataErrors));
        if (!CreateAccessRoleNormalizer.TryNormalize(command.RawBody, out var request, out var requestErrors))
            return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.Validation(requestErrors));

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

        var capabilityErrors = request!.CapabilityInputs
            .Where(item => !AssignableCapabilityCatalog.Contains(item.Value))
            .ToDictionary(
                item => $"capabilities[{item.OriginalIndex}]",
                _ => new[] { "Capability is not assignable to a custom Workspace role." },
                StringComparer.Ordinal);
        if (capabilityErrors.Count != 0)
            return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.Validation(capabilityErrors));

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
                cancellationToken);
            switch (result.Status)
            {
                case CreateAccessRoleCommitStatus.Committed:
                case CreateAccessRoleCommitStatus.Replay:
                    return await ComposeAsync(result.Commit!, cancellationToken);
                case CreateAccessRoleCommitStatus.IdempotencyKeyReused:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.IdempotencyKeyReused(command.IdempotencyKey));
                case CreateAccessRoleCommitStatus.RoleNameConflict:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.RoleNameConflict());
                case CreateAccessRoleCommitStatus.LifecycleConflict:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.LifecycleConflict());
                case CreateAccessRoleCommitStatus.Contention:
                    continue;
                default:
                    throw new InvalidOperationException("Unsupported createAccessRole persistence result.");
            }
        }
        throw new InvalidOperationException("createAccessRole did not converge after database contention.");
    }

    private async Task<AccessOperationResult<AccessMutationResponse>> ComposeAsync(
        CreateAccessRoleCommit commit,
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

    private static CreateAccessRoleCommit Replay(Domain.AccessRoleCommandIdempotencyRecord record) => new(
        record.CommandId,
        record.RoleId,
        record.RoleVersion,
        record.AuditEvidenceId,
        record.EventId,
        record.DirectoryRevisionAtCommit,
        record.CorrelationId,
        record.OccurredAt,
        true);

    private static IReadOnlyDictionary<string, string[]> MetadataErrors(Command command)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (command.RequestId.Length is < 8 or > 128)
            fields["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."];
        if (command.SuppliedCorrelationId.Length != 0 && command.SuppliedCorrelationId.Length is < 8 or > 128)
            fields["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."];
        if (command.IdempotencyKey.Length is < 8 or > 128)
            fields["Idempotency-Key"] = ["Idempotency-Key must contain between 8 and 128 characters."];
        return fields;
    }
}
