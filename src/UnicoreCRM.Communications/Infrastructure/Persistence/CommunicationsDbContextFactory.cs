using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UnicoreCRM.Communications.Infrastructure.Persistence;

internal sealed class CommunicationsDbContextFactory
    : IDesignTimeDbContextFactory<CommunicationsDbContext>
{
    public CommunicationsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__UnicoreCRM")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=UnicoreCRM_Development;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<CommunicationsDbContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "communications"))
            .Options;

        return new CommunicationsDbContext(options);
    }
}
