using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<IWebhookSecretProvider, ConfigurationWebhookSecretProvider>();
        services.AddHostedService<DevelopmentInboundBindingBootstrap>();
        services.AddInboundModule();
        services.AddOutboundModule();
        services.AddProvidersModule();

        return services;
    }
}
