using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        return services;
    }
}
