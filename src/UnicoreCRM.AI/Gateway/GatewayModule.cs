using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.AI.Gateway;

internal static class GatewayModule
{
    internal static IServiceCollection AddGatewayModule(this IServiceCollection services)
    {
        services.AddScoped<AiAdvisoryApplication>();
        return services;
    }
}
