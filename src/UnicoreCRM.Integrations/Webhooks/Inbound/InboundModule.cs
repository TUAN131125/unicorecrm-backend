using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Integrations.Webhooks.Inbound.Application;
using UnicoreCRM.Integrations.Webhooks.Inbound.Infrastructure;

namespace UnicoreCRM.Integrations.Webhooks.Inbound;

internal static class InboundModule
{
    internal static IServiceCollection AddInboundModule(this IServiceCollection services)
    {
        services.AddScoped<GenericSignedJsonVerifier>();
        services.AddScoped<InboundLeadWebhookCoordinator>();
        return services;
    }
}
