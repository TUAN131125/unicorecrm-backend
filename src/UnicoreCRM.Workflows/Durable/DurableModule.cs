using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Workflows.Durable.Infrastructure;

namespace UnicoreCRM.Workflows.Durable;

internal static class DurableModule
{
    internal static IServiceCollection AddDurableModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DurableWorkflowOptions>()
            .Bind(configuration.GetSection(DurableWorkflowOptions.SectionName));
        services.AddScoped<Application.ProvisionInitialWorkspace.InitialWorkspaceAccessCompletion>();
        services.AddScoped<Application.ProvisionInitialWorkspace.Handler>();
        services.AddHostedService<Application.ProvisionInitialWorkspace.InitialWorkspaceProvisioningResumeService>();
        return services;
    }
}
