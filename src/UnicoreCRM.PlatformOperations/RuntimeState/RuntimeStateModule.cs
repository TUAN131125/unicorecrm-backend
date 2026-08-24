using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.PlatformOperations.RuntimeState;

internal static class RuntimeStateModule
{
    internal static IServiceCollection AddRuntimeStateModule(this IServiceCollection services)
    {
        return services;
    }
}
