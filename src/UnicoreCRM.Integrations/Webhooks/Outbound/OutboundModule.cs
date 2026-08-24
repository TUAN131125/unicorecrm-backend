using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.Integrations.Webhooks.Outbound;

internal static class OutboundModule
{
    internal static IServiceCollection AddOutboundModule(this IServiceCollection services)
    {
        return services;
    }
}
