namespace UnicoreCRM.Platform.AccessControl.Infrastructure;

internal sealed class AccessControlOptions
{
    internal const string SectionName = "AccessControl";
    public DevelopmentAccessControlBootstrapOptions DevelopmentBootstrap { get; init; } = new();
}

internal sealed class DevelopmentAccessControlBootstrapOptions
{
    public bool Enabled { get; init; }
    public string IdentityEmail { get; init; } = string.Empty;
    public string WorkspaceKey { get; init; } = string.Empty;
    public string RoleName { get; init; } = "Development Bootstrap";
    public string[] Capabilities { get; init; } = [];
}
