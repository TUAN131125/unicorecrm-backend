namespace UnicoreCRM.Platform.AccessControl.Domain;

internal sealed class RoleDataScopePolicy
{
    private RoleDataScopePolicy() { }

    internal RoleDataScopePolicy(string workspaceId, string roleId, string resourceKey, AccessDataScope scope, string allowedOwnerIdsJson)
    {
        PolicyId = AccessControlIds.New("scope");
        WorkspaceId = workspaceId;
        RoleId = roleId;
        ResourceKey = resourceKey;
        Scope = scope;
        AllowedOwnerIdsJson = allowedOwnerIdsJson;
    }

    public string PolicyId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string RoleId { get; private set; } = null!;
    public string ResourceKey { get; private set; } = null!;
    public AccessDataScope Scope { get; private set; }
    public string AllowedOwnerIdsJson { get; private set; } = null!;

    /// <summary>
    /// Replaces the policy value while preserving the owner-generated <see cref="PolicyId"/>. Policy
    /// identity is stable across a full role replacement for an unchanged canonical
    /// <c>(RoleId, ResourceKey)</c> key, so a replacement that only changes a scope value does not
    /// churn identities that other evidence may reference.
    /// </summary>
    internal void Replace(AccessDataScope scope, string allowedOwnerIdsJson)
    {
        Scope = scope;
        AllowedOwnerIdsJson = allowedOwnerIdsJson;
    }
}

internal enum AccessDataScope
{
    Custom = 0,
    Own = 1,
    Team = 2,
    Workspace = 3
}
