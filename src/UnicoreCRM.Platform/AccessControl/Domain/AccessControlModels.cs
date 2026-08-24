namespace UnicoreCRM.Platform.AccessControl.Domain;

internal sealed class AccessRole
{
    private AccessRole() { }

    internal AccessRole(string workspaceId, string name, string? description, string? sourceTemplateId, DateTimeOffset now)
    {
        RoleId = AccessControlIds.New("role");
        WorkspaceId = workspaceId;
        Name = name;
        Description = description;
        SourceTemplateId = sourceTemplateId;
        IsActive = true;
        Version = 0;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string RoleId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? SourceTemplateId { get; private set; }
    public bool IsActive { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}

internal sealed class RoleCapability
{
    private RoleCapability() { }

    internal RoleCapability(string roleId, string capability)
    {
        RoleId = roleId;
        Capability = capability;
    }

    public string RoleId { get; private set; } = null!;
    public string Capability { get; private set; } = null!;
}

internal sealed class MembershipRoleAssignment
{
    private MembershipRoleAssignment() { }

    internal MembershipRoleAssignment(string workspaceId, string membershipId, string roleId, DateTimeOffset now)
    {
        AssignmentId = AccessControlIds.New("assignment");
        WorkspaceId = workspaceId;
        MembershipId = membershipId;
        RoleId = roleId;
        AssignedAt = now;
    }

    public string AssignmentId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string MembershipId { get; private set; } = null!;
    public string RoleId { get; private set; } = null!;
    public DateTimeOffset AssignedAt { get; private set; }
}

internal sealed class RoleDataScopePolicy
{
    private RoleDataScopePolicy() { }

    internal RoleDataScopePolicy(string roleId, string resourceKey, AccessDataScope scope, string allowedOwnerIdsJson)
    {
        PolicyId = AccessControlIds.New("scope");
        RoleId = roleId;
        ResourceKey = resourceKey;
        Scope = scope;
        AllowedOwnerIdsJson = allowedOwnerIdsJson;
    }

    public string PolicyId { get; private set; } = null!;
    public string RoleId { get; private set; } = null!;
    public string ResourceKey { get; private set; } = null!;
    public AccessDataScope Scope { get; private set; }
    public string AllowedOwnerIdsJson { get; private set; } = null!;
}

internal sealed class RoleFieldSecurityPolicy
{
    private RoleFieldSecurityPolicy() { }

    internal RoleFieldSecurityPolicy(string roleId, string resourceKey, string fieldKey, AccessFieldAccess access)
    {
        PolicyId = AccessControlIds.New("field");
        RoleId = roleId;
        ResourceKey = resourceKey;
        FieldKey = fieldKey;
        Access = access;
    }

    public string PolicyId { get; private set; } = null!;
    public string RoleId { get; private set; } = null!;
    public string ResourceKey { get; private set; } = null!;
    public string FieldKey { get; private set; } = null!;
    public AccessFieldAccess Access { get; private set; }
}

internal sealed class AuthorizationDecisionRecord
{
    private AuthorizationDecisionRecord() { }

    internal AuthorizationDecisionRecord(
        string workspaceId,
        string membershipId,
        string requiredCapability,
        bool allowed,
        string correlationId,
        DateTimeOffset evaluatedAt)
    {
        DecisionId = AccessControlIds.New("decision");
        WorkspaceId = workspaceId;
        MembershipId = membershipId;
        RequiredCapability = requiredCapability;
        Allowed = allowed;
        CorrelationId = correlationId;
        EvaluatedAt = evaluatedAt;
    }

    public string DecisionId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string MembershipId { get; private set; } = null!;
    public string RequiredCapability { get; private set; } = null!;
    public bool Allowed { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public DateTimeOffset EvaluatedAt { get; private set; }
}

internal enum AccessDataScope
{
    Custom = 0,
    Own = 1,
    Team = 2,
    Workspace = 3
}

internal enum AccessFieldAccess
{
    Hidden = 0,
    Masked = 1,
    ReadOnly = 2,
    ReadWrite = 3
}

internal static class AccessControlIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
