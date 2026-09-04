using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.ArchiveAccessRole;

internal sealed record Command(
    string RoleId,
    AdministrativeRequestBody Body,
    string RequestId,
    string CorrelationId,
    string SuppliedCorrelationId,
    string IdempotencyKey,
    string IfMatch);

internal sealed record NormalizedArchiveAccessRole(
    string RoleId,
    long ExpectedVersion,
    string? Reason,
    string Fingerprint);

internal sealed record ArchiveAccessRoleCommit(
    string CommandId,
    string RoleId,
    long RoleVersion,
    string AuditEvidenceId,
    string EventId,
    long DirectoryRevisionAtCommit,
    string CorrelationId,
    DateTimeOffset OccurredAt,
    bool IsReplay);

internal enum ArchiveAccessRoleCommitStatus
{
    Committed,
    Replay,
    IdempotencyKeyReused,
    RoleNotFound,
    RoleInactive,
    VersionConflict,
    LastWorkspaceAdministrator,
    ProviderFactsRequired,
    Contention
}

internal sealed record ArchiveAccessRoleCommitResult(
    ArchiveAccessRoleCommitStatus Status,
    ArchiveAccessRoleCommit? Commit = null);

internal interface IArchiveAccessRolePersistence
{
    Task<AccessRoleCommandIdempotencyRecord?> FindIdempotencyAsync(
        string workspaceId,
        string actorMembershipId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Commits one <c>ROLE_ARCHIVE_TRANSACTION</c>. <paramref name="activeMembershipIds"/> is the
    /// authoritative set of active Workspace memberships, supplied only after the transaction has
    /// established that the target is currently an administrator role; it is null on the first
    /// attempt so a non-administrative archive never touches a foreign provider.
    /// </summary>
    Task<ArchiveAccessRoleCommitResult> TryCommitAsync(
        string workspaceId,
        string actorAccountId,
        string actorMembershipId,
        string actorMemberId,
        string requestId,
        string correlationId,
        string idempotencyKey,
        NormalizedArchiveAccessRole request,
        IReadOnlySet<string>? activeMembershipIds,
        CancellationToken cancellationToken);
}
