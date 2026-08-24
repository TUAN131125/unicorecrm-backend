using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Workflows.Atomic;
using UnicoreCRM.Workflows.Durable;

namespace UnicoreCRM.Workflows;

public static class WorkflowsModule
{
    public static IServiceCollection AddWorkflowsModule(this IServiceCollection services)
    {
        services.AddAtomicModule();
        services.AddDurableModule();

        return services;
    }
}
