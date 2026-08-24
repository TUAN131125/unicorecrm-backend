namespace UnicoreCRM.Platform.AccessControl.Domain;

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
