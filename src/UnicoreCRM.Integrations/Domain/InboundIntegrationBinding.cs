namespace UnicoreCRM.Integrations.Domain;

internal sealed class InboundIntegrationBinding
{
    private InboundIntegrationBinding() { }

    internal InboundIntegrationBinding(
        string integrationId,
        string providerCode,
        string workspaceId,
        string delegatedMemberId,
        string secretReference,
        bool isEnabled,
        DateTimeOffset now)
    {
        IntegrationId = integrationId;
        ProviderCode = providerCode;
        WorkspaceId = workspaceId;
        DelegatedMemberId = delegatedMemberId;
        SecretReference = secretReference;
        IsEnabled = isEnabled;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string IntegrationId { get; private set; } = null!;
    public string ProviderCode { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string DelegatedMemberId { get; private set; } = null!;
    public string SecretReference { get; private set; } = null!;
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
