using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UnicoreCRM.Sales.Orders.Infrastructure.Persistence;

internal sealed class OrdersDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__UnicoreCRM")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=UnicoreCRM;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "orders"))
            .Options;
        return new OrdersDbContext(options);
    }
}
