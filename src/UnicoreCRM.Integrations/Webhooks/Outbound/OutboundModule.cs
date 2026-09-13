using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Integrations.Webhooks.Outbound.Application;
using UnicoreCRM.Integrations.Webhooks.Outbound.Infrastructure;

namespace UnicoreCRM.Integrations.Webhooks.Outbound;

internal static class OutboundModule
{
    internal static IServiceCollection AddOutboundModule(this IServiceCollection services)
    {
        services.AddDataProtection();
        services.AddScoped<OutboundWebhookService>();
        services.AddSingleton<IWebhookHostResolver, DnsWebhookHostResolver>();
        services.AddSingleton<IOutboundWebhookTransport, SafeOutboundWebhookTransport>();
        services.AddHostedService<OutboundWebhookConsumer>();
        services.AddHostedService<OutboundWebhookSender>();
        return services;
    }
}
