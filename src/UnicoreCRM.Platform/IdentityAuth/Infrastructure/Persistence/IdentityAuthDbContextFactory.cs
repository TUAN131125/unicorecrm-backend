using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence;

internal sealed class IdentityAuthDbContextFactory : IDesignTimeDbContextFactory<IdentityAuthDbContext>
{
    public IdentityAuthDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__UnicoreCRM")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=UnicoreCRM_Development;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<IdentityAuthDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "iam"))
            .Options;
        return new IdentityAuthDbContext(options);
    }
}
