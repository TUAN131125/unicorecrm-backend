using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UnicoreCRM.Billing.Invoices.Infrastructure.Persistence;

internal sealed class InvoicesDbContextFactory : IDesignTimeDbContextFactory<InvoicesDbContext>
{
    public InvoicesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__UnicoreCRM")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=UnicoreCRM_Invoices_Design;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<InvoicesDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "invoices"))
            .Options;
        return new InvoicesDbContext(options);
    }
}
