using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;

namespace UnicoreCRM.PlatformOperations.Outbox;

internal static class OutboxModule
{
    internal static IServiceCollection AddOutboxModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<IntegrationEventJournalDbContext>(options => options.UseSqlServer(connectionString,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "ops")));
        services.AddScoped<IntegrationEventJournal>();
        services.AddScoped<IIntegrationEventCatalog, AggregatedIntegrationEventCatalog>();
        services.AddScoped<IIntegrationEventFeed>(provider => provider.GetRequiredService<IntegrationEventJournal>());
        services.Configure<IntegrationEventRelayOptions>(configuration.GetSection(IntegrationEventRelayOptions.Section));
        services.AddHostedService<IntegrationEventRelayWorker>();
        services.AddDevelopmentSchemaMigration("integration-event-journal",
            (provider, cancellationToken) => provider.GetRequiredService<IntegrationEventJournalDbContext>().Database.MigrateAsync(cancellationToken));
        return services;
    }
}
