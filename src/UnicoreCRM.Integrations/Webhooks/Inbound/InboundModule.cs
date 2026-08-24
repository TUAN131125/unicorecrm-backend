using Microsoft.Extensions.DependencyInjection;

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
