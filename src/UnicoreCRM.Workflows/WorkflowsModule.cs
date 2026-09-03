using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Workflows.Atomic;
using UnicoreCRM.Workflows.Durable;

namespace UnicoreCRM.Workflows;

public static class WorkflowsModule
{
    public static IServiceCollection AddWorkflowsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAtomicModule(configuration);
        services.AddDurableModule(configuration);

        return services;
    }
}
