namespace UnicoreCRM.Platform.Workspace.Domain;

internal sealed class WorkspaceBootstrapProjection
{
    private WorkspaceBootstrapProjection() { }

    internal WorkspaceBootstrapProjection(
        string workspaceId,
        string locale,
        string timeZone,
        string baseCurrency,
        string capabilitiesJson,
        string enabledModuleKeysJson,
        string availableProductSpacesJson)
    {
        WorkspaceId = workspaceId;
        ContextVersion = 0;
        ConfigurationVersion = 0;
        Locale = locale;
        TimeZone = timeZone;
        BaseCurrency = baseCurrency;
        CapabilitiesJson = capabilitiesJson;
        EnabledModuleKeysJson = enabledModuleKeysJson;
        AvailableProductSpacesJson = availableProductSpacesJson;
    }

    public string WorkspaceId { get; private set; } = null!;
    public long ContextVersion { get; private set; }
    public long ConfigurationVersion { get; private set; }
    public string Locale { get; private set; } = null!;
    public string TimeZone { get; private set; } = null!;
    public string BaseCurrency { get; private set; } = null!;
    public string CapabilitiesJson { get; private set; } = null!;
    public string EnabledModuleKeysJson { get; private set; } = null!;
    public string AvailableProductSpacesJson { get; private set; } = null!;
}
