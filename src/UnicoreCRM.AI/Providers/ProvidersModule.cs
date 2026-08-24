using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace UnicoreCRM.AI.Providers;

internal static class ProvidersModule
{
    internal static IServiceCollection AddProvidersModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var timeoutSeconds = Math.Clamp(configuration.GetValue("AI:Provider:TimeoutSeconds", 10), 1, 60);
        services.AddSingleton(new AiProviderRuntimeOptions(TimeSpan.FromSeconds(timeoutSeconds)));
        services.AddSingleton<AiProviderOutputValidator>();

        var kind = configuration["AI:Provider:Kind"];
        if (environment.IsDevelopment()
            && string.Equals(kind, "DevelopmentDeterministic", StringComparison.Ordinal))
        {
            var mode = configuration["AI:Provider:DevelopmentMode"] ?? "Normal";
            services.AddSingleton<IAiProvider>(new DevelopmentDeterministicAiProvider(mode));
        }
        else
        {
            services.AddSingleton<IAiProvider, UnavailableAiProvider>();
        }

        return services;
    }
}
