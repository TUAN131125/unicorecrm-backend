using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Communications.Application.Common;
using UnicoreCRM.Communications.Infrastructure.Persistence;

namespace UnicoreCRM.Communications;

public static class CommunicationsModule
{
    public static IServiceCollection AddCommunicationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");

        services.AddDbContext<CommunicationsDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "communications")));

        services.AddScoped<ICommunicationsPersistence, EfCommunicationsPersistence>();
        services.AddDevelopmentSchemaMigration(
            "communications",
            (provider, cancellationToken) =>
                provider.GetRequiredService<CommunicationsDbContext>()
                    .Database.MigrateAsync(cancellationToken));

        services.AddScoped<Application.ListSenderProfiles.Handler>();
        services.AddScoped<Application.ListEmailConnections.Handler>();

        return services;
    }
}
