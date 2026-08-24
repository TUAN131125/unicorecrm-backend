using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.Workflows.Durable;

internal static class DurableModule
{
    internal static IServiceCollection AddDurableModule(this IServiceCollection services)
    {
        return services;
    }
}
