using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.ReplaceWorkspaceMemberAccess;

internal sealed record Command(
    string MembershipId,
    AdministrativeRequestBody Body,
    string RequestId,
    string CorrelationId,
    string SuppliedCorrelationId,
    string IdempotencyKey,
    string IfMatch);

internal sealed record NormalizedReplaceWorkspaceMemberAccess(
    string MembershipId,
    long ExpectedMemberAccessVersion,
    IReadOnlyList<string> RoleIds,
    string Fingerprint);

internal sealed record WorkspaceMemberAccessFacts(
    IReadOnlySet<string> ActiveMembershipIds);

internal sealed record ReplaceWorkspaceMemberAccessCommit(
    string CommandId,
    string MembershipId,
    long MemberAccessVersion,
    string AuditEvidenceId,
    string EventId,
    long DirectoryRevisionAtCommit,
    string CorrelationId,
    DateTimeOffset OccurredAt,
    bool IsReplay);

internal enum ReplaceWorkspaceMemberAccessCommitStatus
{
    Committed,
    Replay,
    IdempotencyKeyReused,
    VersionConflict,
    RoleNotFound,
    RoleInactive,
    LastWorkspaceAdministrator,
    Contention
}

internal sealed record ReplaceWorkspaceMemberAccessCommitResult(
    ReplaceWorkspaceMemberAccessCommitStatus Status,
    ReplaceWorkspaceMemberAccessCommit? Commit = null);

internal interface IReplaceWorkspaceMemberAccessPersistence
{
    Task<MemberAccessCommandIdempotencyRecord?> FindIdempotencyAsync(
        string workspaceId,
        string actorMembershipId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ReplaceWorkspaceMemberAccessCommitResult> TryCommitAsync(
        string workspaceId,
        string actorAccountId,
        string actorMembershipId,
        string actorMemberId,
        string requestId,
        string correlationId,
        string idempotencyKey,
        NormalizedReplaceWorkspaceMemberAccess request,
        IReadOnlySet<string> activeMembershipIds,
        CancellationToken cancellationToken);
}
