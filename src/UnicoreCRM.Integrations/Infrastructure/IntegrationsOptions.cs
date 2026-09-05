namespace UnicoreCRM.Integrations.Infrastructure;

internal sealed class IntegrationsOptions
{
    internal const string SectionName = "Integrations";
    public DevelopmentInboundBindingOptions DevelopmentBootstrap { get; init; } = new();
}

internal sealed class DevelopmentInboundBindingOptions
{
    public bool Enabled { get; init; }
    public string IntegrationId { get; init; } = string.Empty;
    public string ProviderCode { get; init; } = "generic-signed-json";
    public string WorkspaceId { get; init; } = string.Empty;
    public string DelegatedMemberId { get; init; } = string.Empty;
    public string SecretReference { get; init; } = string.Empty;
    public bool BindingEnabled { get; init; } = true;
}
