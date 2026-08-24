using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.AI.Context;

internal static class ContextModule
{
    internal static IServiceCollection AddContextModule(this IServiceCollection services)
    {
        services.AddScoped<AiContextComposer>();
        return services;
    }
}
