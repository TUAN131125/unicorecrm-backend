using UnicoreCRM.Integrations.Domain;

namespace UnicoreCRM.Integrations.Application;

internal interface IInboundIntegrationBindingStore
{
    Task<InboundIntegrationBinding?> FindAsync(
        string integrationId,
        CancellationToken cancellationToken);
}

internal interface IWebhookSecretProvider
{
    string? Resolve(string secretReference);
}
