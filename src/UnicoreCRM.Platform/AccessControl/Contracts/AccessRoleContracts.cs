using System.Text.Json.Serialization;

namespace UnicoreCRM.Platform.AccessControl.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAccessRoleRequest(
    string? Name,
    IReadOnlyList<string?>? Capabilities,
    IReadOnlyList<AccessRoleDataScopeInput?>? DataScopes,
    IReadOnlyList<AccessRoleFieldSecurityInput?>? FieldSecurity,
    string? Description = null,
    string? SourceTemplateId = null);

/// <summary>
/// The lifecycle deactivation request. <c>reason</c> is optional explanatory governance provenance
/// only: it is persisted solely in the archive governance audit and influences no business rule.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArchiveAccessRoleRequest(string? Reason = null);

/// <summary>
/// The full replacement of a role's mutable configuration. Every replaceable collection is required
/// by the wire contract, so there is no preserve-on-omission behavior; an omitted optional scalar is
/// the canonical null. <c>IsActive</c> is nullable only so an omitted required property is reported
/// as a missing field rather than silently defaulting to false.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceAccessRoleRequest(
    string? Name,
    bool? IsActive,
    IReadOnlyList<string?>? Capabilities,
    IReadOnlyList<AccessRoleDataScopeInput?>? DataScopes,
    IReadOnlyList<AccessRoleFieldSecurityInput?>? FieldSecurity,
    string? Description = null,
    string? SourceTemplateId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccessRoleDataScopeInput(
    string? ResourceKey,
    string? Scope,
    IReadOnlyList<string?>? AllowedOwnerIds = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccessRoleFieldSecurityInput(
    string? ResourceKey,
    string? FieldKey,
    string? Access);

public sealed record AccessMutationResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    DateTimeOffset OccurredAt,
    string Outcome,
    WorkspaceAccessDirectoryDocument Result,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,
    IReadOnlyList<string> AuditEvidenceIds);

public sealed record WorkspaceAccessDirectoryDocument(
    string WorkspaceId,
    long Revision,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<WorkspaceAccessMemberDocument> Members,
    IReadOnlyList<WorkspaceMemberProfileDocument> MemberProfiles,
    IReadOnlyList<WorkspaceInvitationDocument> Invitations,
    IReadOnlyList<AccessRoleDocument> Roles,
    IReadOnlyList<RoleAssignmentDocument> Assignments,
    IReadOnlyList<AccessDataScopePolicyDocument> DataScopes,
    IReadOnlyList<AccessFieldSecurityPolicyDocument> FieldSecurity);

public sealed record WorkspaceAccessMemberDocument(
    string MembershipId,
    string MemberId,
    string WorkspaceId,
    string WorkspaceKey,
    string Name,
    string Status,
    string LogoText,
    IReadOnlyList<string> TeamIds,
    IReadOnlyList<string> RoleIds,
    string Source,
    long Version,
    string? AccountId = null,
    DateTimeOffset? CreatedAt = null);

public sealed record WorkspaceMemberProfileDocument(
    string MemberId,
    string MembershipId,
    string DisplayName,
    string AccountSource,
    string? AccountId = null,
    string? Email = null,
    string? AccountStatus = null,
    string? RoleLabel = null,
    DateTimeOffset? ProvisionedAt = null);

public sealed record WorkspaceInvitationDocument(
    string InvitationId,
    string MembershipId,
    string WorkspaceId,
    string Email,
    string DisplayName,
    IReadOnlyList<string> RoleIds,
    IReadOnlyList<string> TeamIds,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSentAt,
    DateTimeOffset ExpiresAt,
    long Version,
    DateTimeOffset? AcceptedAt = null,
    DateTimeOffset? RevokedAt = null);

public sealed record AccessRoleDocument(
    string RoleId,
    string WorkspaceId,
    string Name,
    bool IsActive,
    IReadOnlyList<string> Capabilities,
    long Version,
    string? Description = null,
    string? SourceTemplateId = null);

public sealed record RoleAssignmentDocument(
    string AssignmentId,
    string WorkspaceId,
    string MembershipId,
    string RoleId);

public sealed record AccessDataScopePolicyDocument(
    string PolicyId,
    string WorkspaceId,
    string RoleId,
    string ResourceKey,
    string Scope,
    IReadOnlyList<string>? AllowedOwnerIds = null);

public sealed record AccessFieldSecurityPolicyDocument(
    string PolicyId,
    string WorkspaceId,
    string RoleId,
    string ResourceKey,
    string FieldKey,
    string Access);
