using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.Workflows.Atomic;

internal static class AtomicModule
{
    internal static IServiceCollection AddAtomicModule(this IServiceCollection services)
    {
        services.AddScoped<Application.ProvisionInitialWorkspace.Handler>();
        return services;
    }
}
