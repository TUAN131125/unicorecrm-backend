namespace UnicoreCRM.Platform.AccessControl.Domain;

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

internal enum AccessFieldAccess
{
    Hidden = 0,
    Masked = 1,
    ReadOnly = 2,
    ReadWrite = 3
}
