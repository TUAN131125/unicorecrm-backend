using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using UnicoreCRM.PlatformOperations.Outbox;
using UnicoreCRM.PlatformOperations.Inbox;
using UnicoreCRM.PlatformOperations.Idempotency;
using UnicoreCRM.PlatformOperations.BackgroundJobs;
using UnicoreCRM.PlatformOperations.RuntimeState;
using UnicoreCRM.PlatformOperations.AiExecution;

namespace UnicoreCRM.PlatformOperations;

public static class PlatformOperationsModule
{
    public static IServiceCollection AddPlatformOperationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOutboxModule(configuration);
        services.AddInboxModule(configuration);
        services.AddIdempotencyModule();
        services.AddBackgroundJobsModule();
        services.AddRuntimeStateModule();
        services.AddAiExecutionModule(configuration);

        return services;
    }
}
