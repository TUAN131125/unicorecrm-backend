using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.PlatformOperations.Idempotency;

internal static class IdempotencyModule
{
    internal static IServiceCollection AddIdempotencyModule(this IServiceCollection services)
    {
        return services;
    }
}
