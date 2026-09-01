using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.CreateAccessRole;

internal sealed record Command(
    string RawBody,
    string RequestId,
    string CorrelationId,
    string SuppliedCorrelationId,
    string IdempotencyKey);

internal sealed record NormalizedCreateAccessRole(
    string Name,
    string NormalizedName,
    string? Description,
    string? SourceTemplateId,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<NormalizedCapabilityInput> CapabilityInputs,
    IReadOnlyList<NormalizedDataScope> DataScopes,
    IReadOnlyList<NormalizedFieldSecurity> FieldSecurity,
    string Fingerprint);

internal sealed record NormalizedCapabilityInput(string Value, int OriginalIndex);

internal sealed record NormalizedDataScope(
    string ResourceKey,
    AccessDataScope Scope,
    IReadOnlyList<string> AllowedOwnerIds);

internal sealed record NormalizedFieldSecurity(
    string ResourceKey,
    string FieldKey,
    AccessFieldAccess Access);

internal sealed record CreateAccessRoleCommit(
    string CommandId,
    string RoleId,
    long RoleVersion,
    string AuditEvidenceId,
    string EventId,
    long DirectoryRevisionAtCommit,
    string CorrelationId,
    DateTimeOffset OccurredAt,
    bool IsReplay);

internal enum CreateAccessRoleCommitStatus
{
    Committed,
    Replay,
    IdempotencyKeyReused,
    RoleNameConflict,
    LifecycleConflict,
    Contention
}

internal sealed record CreateAccessRoleCommitResult(
    CreateAccessRoleCommitStatus Status,
    CreateAccessRoleCommit? Commit = null);

internal sealed record AccessControlDirectoryState(
    long Revision,
    IReadOnlyList<AccessRole> Roles,
    IReadOnlyList<RoleCapability> Capabilities,
    IReadOnlyList<MembershipRoleAssignment> Assignments,
    IReadOnlyList<RoleDataScopePolicy> DataScopes,
    IReadOnlyList<RoleFieldSecurityPolicy> FieldSecurity);

internal interface ICreateAccessRolePersistence
{
    Task<AccessRoleCommandIdempotencyRecord?> FindIdempotencyAsync(
        string workspaceId,
        string actorMembershipId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<CreateAccessRoleCommitResult> TryCommitAsync(
        string workspaceId,
        string actorAccountId,
        string actorMembershipId,
        string actorMemberId,
        string requestId,
        string correlationId,
        string idempotencyKey,
        NormalizedCreateAccessRole request,
        CancellationToken cancellationToken);

    Task<AccessControlDirectoryState?> ReadDirectoryAsync(
        string workspaceId,
        CancellationToken cancellationToken);
}
