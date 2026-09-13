using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UnicoreCRM.PlatformOperations.Outbox;

internal sealed class IntegrationEventJournalDbContextFactory : IDesignTimeDbContextFactory<IntegrationEventJournalDbContext>
{
    public IntegrationEventJournalDbContext CreateDbContext(string[] args)
    {
        var connectionString=Environment.GetEnvironmentVariable("ConnectionStrings__UnicoreCRM")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=UnicoreCRM_Development;Trusted_Connection=True;TrustServerCertificate=True";
        return new(new DbContextOptionsBuilder<IntegrationEventJournalDbContext>().UseSqlServer(connectionString,
            sql=>sql.MigrationsHistoryTable("__EFMigrationsHistory","ops")).Options);
    }
}
