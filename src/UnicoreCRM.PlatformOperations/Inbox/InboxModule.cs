using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.PlatformOperations.Inbox.Contracts;
using UnicoreCRM.PlatformOperations.Inbox.Infrastructure.Persistence;

namespace UnicoreCRM.PlatformOperations.Inbox;

internal static class InboxModule
{
    internal static IServiceCollection AddInboxModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<InboxDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "ops")));
        services.AddScoped<IInboundDeliveryInbox, EfInboundDeliveryInbox>();
        return services;
    }
}
