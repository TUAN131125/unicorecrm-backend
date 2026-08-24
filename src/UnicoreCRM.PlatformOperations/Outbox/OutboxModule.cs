using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.PlatformOperations.Outbox;

internal static class OutboxModule
{
    internal static IServiceCollection AddOutboxModule(this IServiceCollection services)
    {
        return services;
    }
}
