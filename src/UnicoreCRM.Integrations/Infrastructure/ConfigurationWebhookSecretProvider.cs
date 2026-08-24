using Microsoft.Extensions.Configuration;
using UnicoreCRM.Integrations.Application;

namespace UnicoreCRM.Integrations.Infrastructure;

internal sealed class ConfigurationWebhookSecretProvider(IConfiguration configuration) : IWebhookSecretProvider
{
    public string? Resolve(string secretReference)
    {
        var value = configuration[$"Integrations:Secrets:{secretReference}"];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
