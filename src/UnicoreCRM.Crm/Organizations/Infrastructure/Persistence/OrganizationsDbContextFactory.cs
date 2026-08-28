using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UnicoreCRM.Crm.Organizations.Infrastructure.Persistence;

internal sealed class OrganizationsDbContextFactory : IDesignTimeDbContextFactory<OrganizationsDbContext>
{
    public OrganizationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__UnicoreCRM")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=UnicoreCRM;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<OrganizationsDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "organizations"))
            .Options;
        return new OrganizationsDbContext(options);
    }
}
