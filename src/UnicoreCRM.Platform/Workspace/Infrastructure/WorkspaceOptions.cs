namespace UnicoreCRM.Platform.Workspace.Infrastructure;

internal sealed class WorkspaceOptions
{
    internal const string SectionName = "Workspace";
    public DevelopmentWorkspaceBootstrapOptions DevelopmentBootstrap { get; init; } = new();
}

internal sealed class DevelopmentWorkspaceBootstrapOptions
{
    public bool Enabled { get; init; }
    public bool ApplyMigrations { get; init; }
    public string IdentityEmail { get; init; } = string.Empty;
    public DevelopmentWorkspaceOptions MemberWorkspace { get; init; } = new();
    public DevelopmentWorkspaceOptions NonMemberWorkspace { get; init; } = new();
}

internal sealed class DevelopmentWorkspaceOptions
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string LogoText { get; init; } = string.Empty;
    public string Locale { get; init; } = "en";
    public string TimeZone { get; init; } = "UTC";
    public string BaseCurrency { get; init; } = "USD";
    public string[] Capabilities { get; init; } = [];
    public string[] EnabledModuleKeys { get; init; } = [];
    public string[] AvailableProductSpaces { get; init; } = ["crm"];
}
