using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UnicoreCRM.AI.Context;
using UnicoreCRM.AI.Gateway;
using UnicoreCRM.AI.Prompts;
using UnicoreCRM.AI.Providers;
using UnicoreCRM.AI.Tools;
using UnicoreCRM.AI.Usage;

namespace UnicoreCRM.AI;

public static class AIModule
{
    public static IServiceCollection AddAIModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddGatewayModule();
        services.AddContextModule();
        services.AddPromptsModule();
        services.AddToolsModule();
        services.AddProvidersModule(configuration, environment);
        services.AddUsageModule();

        return services;
    }
}
