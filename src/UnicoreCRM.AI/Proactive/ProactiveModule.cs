using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.AI.Proactive.Application;
using UnicoreCRM.AI.Proactive.Infrastructure;

namespace UnicoreCRM.AI.Proactive;

internal static class ProactiveModule
{
    internal static IServiceCollection AddProactiveModule(this IServiceCollection services)
    {
        services.AddScoped<CustomerHealthRiskReconciler>();
        services.AddScoped<ProactiveWorkspaceEvaluator>();
        services.AddHostedService<ProactiveEvaluationWorker>();
        return services;
    }
}
