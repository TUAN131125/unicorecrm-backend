using System.Text.RegularExpressions;
using UnicoreCRM.Platform.AccessControl.Application.AccessDirectory;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.ReplaceWorkspaceMemberAccess;

/// <summary>
/// Implements the frozen precedence for <c>POST /access/members/{membershipId}/access</c>. Provider
/// membership facts are read only after authorization, metadata, request normalization and
/// idempotency, while all AccessControl validation and mutation is serialized in the owner
/// transaction.
/// </summary>
internal sealed partial class Handler(
    IAccessContextAuthorizer authorizer,
    ICurrentWorkspace currentWorkspace,
    IReplaceWorkspaceMemberAccessPersistence persistence,
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

        var metadataErrors = MemberAccessCommandMetadata.Validate(
            command.RequestId,
            command.SuppliedCorrelationId,
            command.IdempotencyKey,
            command.IfMatch,
            out var expectedMemberAccessVersion);
        if (metadataErrors.Count != 0)
            return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.Validation(metadataErrors));

        if (!ReplaceWorkspaceMemberAccessNormalizer.TryNormalize(
                command.MembershipId,
                expectedMemberAccessVersion,
                command.RawBody,
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

        var factsResult = await ReadMembershipFactsAsync(
            trusted.WorkspaceId,
            request!.MembershipId,
            cancellationToken);
        if (!factsResult.IsSuccess)
            return AccessOperationResult<AccessMutationResponse>.Failure(factsResult.Error!);

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
                factsResult.Value!.ActiveMembershipIds,
                cancellationToken);
            switch (result.Status)
            {
                case ReplaceWorkspaceMemberAccessCommitStatus.Committed:
                case ReplaceWorkspaceMemberAccessCommitStatus.Replay:
                    return await ComposeAsync(result.Commit!, cancellationToken);
                case ReplaceWorkspaceMemberAccessCommitStatus.IdempotencyKeyReused:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.IdempotencyKeyReused(command.IdempotencyKey));
                case ReplaceWorkspaceMemberAccessCommitStatus.VersionConflict:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.VersionConflict());
                case ReplaceWorkspaceMemberAccessCommitStatus.RoleNotFound:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.ResourceNotFound());
                case ReplaceWorkspaceMemberAccessCommitStatus.RoleInactive:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.RoleInactive());
                case ReplaceWorkspaceMemberAccessCommitStatus.LastWorkspaceAdministrator:
                    return AccessOperationResult<AccessMutationResponse>.Failure(AccessErrors.LastWorkspaceAdministrator());
                case ReplaceWorkspaceMemberAccessCommitStatus.Contention:
                    continue;
                default:
                    throw new InvalidOperationException("Unsupported replaceWorkspaceMemberAccess persistence result.");
            }
        }
        throw new InvalidOperationException("replaceWorkspaceMemberAccess did not converge after database contention.");
    }

    private async Task<AccessOperationResult<WorkspaceMemberAccessFacts>> ReadMembershipFactsAsync(
        string workspaceId,
        string targetMembershipId,
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
            return AccessOperationResult<WorkspaceMemberAccessFacts>.Failure(AccessErrors.IntegrationUnavailable());
        }

        if (!Valid(snapshot, workspaceId))
            return AccessOperationResult<WorkspaceMemberAccessFacts>.Failure(AccessErrors.IntegrationUnavailable());
        if (!snapshot!.Memberships.Any(item => string.Equals(item.MembershipId, targetMembershipId, StringComparison.Ordinal)))
            return AccessOperationResult<WorkspaceMemberAccessFacts>.Failure(AccessErrors.ResourceNotFound());

        var activeMembershipIds = snapshot.Memberships
            .Where(item => string.Equals(item.Status, "active", StringComparison.Ordinal))
            .Select(item => item.MembershipId)
            .ToHashSet(StringComparer.Ordinal);
        return AccessOperationResult<WorkspaceMemberAccessFacts>.Success(
            new WorkspaceMemberAccessFacts(activeMembershipIds));
    }

    private static bool Valid(WorkspaceAccessDirectorySnapshot? snapshot, string workspaceId)
    {
        if (snapshot is null
            || !string.Equals(snapshot.WorkspaceId, workspaceId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(snapshot.WorkspaceKey)
            || string.IsNullOrWhiteSpace(snapshot.Name)
            || string.IsNullOrWhiteSpace(snapshot.LogoText)
            || snapshot.Memberships.GroupBy(item => item.MembershipId, StringComparer.Ordinal).Any(group => group.Count() != 1)
            || snapshot.Memberships.GroupBy(item => item.MemberId, StringComparer.Ordinal).Any(group => group.Count() != 1)
            || snapshot.Invitations.GroupBy(item => item.InvitationId, StringComparer.Ordinal).Any(group => group.Count() != 1)
            || snapshot.Invitations.Any(item => !string.Equals(item.WorkspaceId, workspaceId, StringComparison.Ordinal)))
        {
            return false;
        }

        return snapshot.Memberships.All(item =>
            EntityIdPattern().IsMatch(item.MembershipId)
            && EntityIdPattern().IsMatch(item.MemberId)
            && item.Status is "active" or "suspended"
            && item.Source is "seed" or "invitation" or "direct_provisioning" or "external_identity");
    }

    private async Task<AccessOperationResult<AccessMutationResponse>> ComposeAsync(
        ReplaceWorkspaceMemberAccessCommit commit,
        CancellationToken cancellationToken)
    {
        var directory = await composer.ComposeAsync(currentWorkspace.Require().WorkspaceId, cancellationToken);
        if (!directory.IsSuccess)
            return AccessOperationResult<AccessMutationResponse>.Failure(directory.Error!);
        return AccessOperationResult<AccessMutationResponse>.Success(new AccessMutationResponse(
            commit.CommandId,
            commit.CorrelationId,
            commit.MembershipId,
            "WORKSPACE_MEMBER_ACCESS",
            commit.MemberAccessVersion,
            commit.OccurredAt,
            commit.IsReplay ? "REPLAYED" : "COMMITTED",
            directory.Value!,
            [],
            [commit.EventId],
            [commit.AuditEvidenceId]));
    }

    private static ReplaceWorkspaceMemberAccessCommit Replay(Domain.MemberAccessCommandIdempotencyRecord record) => new(
        record.CommandId,
        record.MembershipId,
        record.MemberAccessVersion,
        record.AuditEvidenceId,
        record.EventId,
        record.DirectoryRevisionAtCommit,
        record.CorrelationId,
        record.OccurredAt,
        true);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
