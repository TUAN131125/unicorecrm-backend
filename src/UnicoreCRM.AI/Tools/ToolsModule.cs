using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.AI.Tools;

internal static class ToolsModule
{
    internal static IServiceCollection AddToolsModule(this IServiceCollection services)
    {
        services.AddScoped<IAiContextTool, LeadSummaryTool>();
        services.AddScoped<IAiContextTool, DealSummaryTool>();
        services.AddScoped<IAiContextTool, TaskSummaryTool>();
        services.AddScoped<AiToolRegistry>();
        return services;
    }
}
