using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.AI.Prompts;

internal static class PromptsModule
{
    internal static IServiceCollection AddPromptsModule(this IServiceCollection services)
    {
        services.AddSingleton<AiPromptComposer>();
        return services;
    }
}
