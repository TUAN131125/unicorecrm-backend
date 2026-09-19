using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.AI.Usage;

internal static class UsageModule
{
    internal static IServiceCollection AddUsageModule(this IServiceCollection services)
    {
        services.AddScoped<IAiUsageRecorder, DurableAiUsageRecorder>();
        return services;
    }
}
