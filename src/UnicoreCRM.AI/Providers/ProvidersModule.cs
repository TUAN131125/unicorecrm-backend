using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.DataProtection;

namespace UnicoreCRM.AI.Providers;

internal static class ProvidersModule
{
    internal static IServiceCollection AddProvidersModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var timeoutSeconds = Math.Clamp(configuration.GetValue("AI:Provider:TimeoutSeconds", 10), 1, 60);
        // This budget applies only to Gemini synthetic tests; OpenAI and Chat retain TimeoutSeconds.
        var testTimeoutSeconds = Math.Clamp(configuration.GetValue("AI:Provider:ConfigurationTestTimeoutSeconds", Math.Max(30, timeoutSeconds)), 1, 60);
        services.AddSingleton(new AiProviderRuntimeOptions(TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromSeconds(testTimeoutSeconds)));
        services.AddSingleton<AiProviderOutputValidator>();
        services.AddSingleton<AiProviderCatalog>();
        var dataProtection = services.AddDataProtection().SetApplicationName("UnicoreCRM.AI");
        var keyRingPath = configuration["AI:DataProtection:KeyRingPath"]?.Trim();
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            var resolvedKeyRingPath = Path.IsPathRooted(keyRingPath)
                ? Path.GetFullPath(keyRingPath)
                : Path.GetFullPath(keyRingPath, environment.ContentRootPath);
            Directory.CreateDirectory(resolvedKeyRingPath);
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(resolvedKeyRingPath));
            if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
        }
        else if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("AI:DataProtection:KeyRingPath must identify a durable production key-ring location.");
        }
        var deterministicTransport = environment.IsDevelopment() && configuration.GetValue("AI:ProviderTesting:UseDeterministicTransport", false);
        // HttpClient's finite 100-second default exceeds the 60-second application cap.
        // The application token also bounds response-body reads (ResponseHeadersRead).
        var gemini = services.AddHttpClient<GeminiAiProvider>(client => client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/"));
        var openAi = services.AddHttpClient<OpenAiProvider>(client => client.BaseAddress = new Uri("https://api.openai.com/"));
        if (deterministicTransport)
        {
            gemini.ConfigurePrimaryHttpMessageHandler(() => new DevelopmentDeterministicProviderHttpHandler());
            openAi.ConfigurePrimaryHttpMessageHandler(() => new DevelopmentDeterministicProviderHttpHandler());
        }
        services.AddScoped<IProductionAiProviderAdapter>(provider => provider.GetRequiredService<GeminiAiProvider>());
        services.AddScoped<IProductionAiProviderAdapter>(provider => provider.GetRequiredService<OpenAiProvider>());
        services.AddScoped<WorkspaceAiProviderResolver>();
        services.AddSingleton<AiProviderCircuitBreaker>();
        services.AddSingleton(new AiWorkspaceGuardrails(TimeProvider.System,
            Math.Clamp(configuration.GetValue("AI:Guardrails:WorkspaceConcurrency", 4), 1, 32),
            Math.Clamp(configuration.GetValue("AI:Guardrails:WorkspaceRequestsPerMinute", 60), 1, 1000)));

        var kind = configuration["AI:Provider:Kind"];
        if (environment.IsDevelopment()
            && string.Equals(kind, "DevelopmentDeterministic", StringComparison.Ordinal))
        {
            var mode = configuration["AI:Provider:DevelopmentMode"] ?? "Normal";
            services.AddSingleton<IAiProvider>(new DevelopmentDeterministicAiProvider(mode, TimeSpan.FromSeconds(timeoutSeconds)));
        }
        else
        {
            services.AddScoped<IAiProvider, WorkspaceProductionAiProvider>();
        }

        return services;
    }
}
