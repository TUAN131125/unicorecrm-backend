namespace UnicoreCRM.Platform.AccessControl.Domain;

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

internal enum AccessDataScope
{
    Custom = 0,
    Own = 1,
    Team = 2,
    Workspace = 3
}
