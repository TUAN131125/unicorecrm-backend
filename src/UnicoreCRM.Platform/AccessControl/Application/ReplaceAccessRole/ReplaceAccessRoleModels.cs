using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.ReplaceAccessRole;

internal sealed record Command(
    string RoleId,
    string RawBody,
    string RequestId,
    string CorrelationId,
    string SuppliedCorrelationId,
    string IdempotencyKey,
    string IfMatch);

internal sealed record NormalizedReplaceAccessRole(
    string RoleId,
    long ExpectedVersion,
    string Name,
    string NormalizedName,
    string? Description,
    string? SourceTemplateId,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<NormalizedCapabilityInput> CapabilityInputs,
    IReadOnlyList<NormalizedDataScope> DataScopes,
    IReadOnlyList<NormalizedFieldSecurity> FieldSecurity,
    string Fingerprint);

internal sealed record ReplaceAccessRoleCommit(
    string CommandId,
    string RoleId,
    long RoleVersion,
    string AuditEvidenceId,
    string EventId,
    long DirectoryRevisionAtCommit,
    string CorrelationId,
    DateTimeOffset OccurredAt,
    bool IsReplay);

internal enum ReplaceAccessRoleCommitStatus
{
    Committed,
    Replay,
    IdempotencyKeyReused,
    RoleNotFound,
    RoleInactive,
    VersionConflict,
    RoleNameConflict,
    LastWorkspaceAdministrator,
    LifecycleConflict,
    ProviderFactsRequired,
    Contention
}

internal sealed record ReplaceAccessRoleCommitResult(
    ReplaceAccessRoleCommitStatus Status,
    ReplaceAccessRoleCommit? Commit = null);

internal interface IReplaceAccessRolePersistence
{
    Task<AccessRoleCommandIdempotencyRecord?> FindIdempotencyAsync(
        string workspaceId,
        string actorMembershipId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Commits one <c>ROLE_REPLACEMENT_TRANSACTION</c>. <paramref name="activeMembershipIds"/> is
    /// the authoritative set of active Workspace memberships, supplied only when the
    /// last-administrator guard is engaged; it is null when the replacement keeps
    /// <c>access.configure</c> and the command therefore stays entirely owner-local.
    /// </summary>
    Task<ReplaceAccessRoleCommitResult> TryCommitAsync(
        string workspaceId,
        string actorAccountId,
        string actorMembershipId,
        string actorMemberId,
        string requestId,
        string correlationId,
        string idempotencyKey,
        NormalizedReplaceAccessRole request,
        IReadOnlySet<string>? activeMembershipIds,
        CancellationToken cancellationToken);
}
