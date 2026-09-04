using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Integrations.Application;
using UnicoreCRM.Integrations.Infrastructure;
using UnicoreCRM.Integrations.Infrastructure.Persistence;
using UnicoreCRM.Integrations.Webhooks.Inbound;
using UnicoreCRM.Integrations.Webhooks.Outbound;
using UnicoreCRM.Integrations.Providers;

namespace UnicoreCRM.Integrations;

public static class IntegrationsModule
{
    public static IServiceCollection AddIntegrationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IntegrationsOptions>()
            .Bind(configuration.GetSection(IntegrationsOptions.SectionName));
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<IntegrationsDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "integration")));
        services.AddScoped<IInboundIntegrationBindingStore, EfInboundIntegrationBindingStore>();
        services.AddDevelopmentSchemaMigration(
            "integrations",
            (provider, cancellationToken) => provider.GetRequiredService<IntegrationsDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddSingleton<IWebhookSecretProvider, ConfigurationWebhookSecretProvider>();
        services.AddScoped<DevelopmentInboundBindingBootstrap>();
        services.AddDevelopmentBootstrapAction(
            "integrations",
            (provider, cancellationToken) => provider.GetRequiredService<DevelopmentInboundBindingBootstrap>().RunAsync(cancellationToken));
        services.AddInboundModule();
        services.AddOutboundModule();
        services.AddProvidersModule();

        return services;
    }
}
