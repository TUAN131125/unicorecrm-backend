namespace UnicoreCRM.Platform.AccessControl.Domain;

internal sealed class RoleFieldSecurityPolicy
{
    private RoleFieldSecurityPolicy() { }

    internal RoleFieldSecurityPolicy(string workspaceId, string roleId, string resourceKey, string fieldKey, AccessFieldAccess access)
    {
        PolicyId = AccessControlIds.New("field");
        WorkspaceId = workspaceId;
        RoleId = roleId;
        ResourceKey = resourceKey;
        FieldKey = fieldKey;
        Access = access;
    }

    public string PolicyId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string RoleId { get; private set; } = null!;
    public string ResourceKey { get; private set; } = null!;
    public string FieldKey { get; private set; } = null!;
    public AccessFieldAccess Access { get; private set; }

    /// <summary>
    /// Replaces the access value while preserving the owner-generated <see cref="PolicyId"/> for an
    /// unchanged canonical <c>(RoleId, ResourceKey, FieldKey)</c> key.
    /// </summary>
    internal void Replace(AccessFieldAccess access) => Access = access;
}

internal enum AccessFieldAccess
{
    Hidden = 0,
    Masked = 1,
    ReadOnly = 2,
    ReadWrite = 3
}
