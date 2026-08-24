using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.PlatformOperations.BackgroundJobs;

internal static class BackgroundJobsModule
{
    internal static IServiceCollection AddBackgroundJobsModule(this IServiceCollection services)
    {
        return services;
    }
}
