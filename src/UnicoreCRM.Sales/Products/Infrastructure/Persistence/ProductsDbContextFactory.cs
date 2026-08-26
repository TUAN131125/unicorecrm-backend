using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UnicoreCRM.Sales.Products.Infrastructure.Persistence;

internal sealed class ProductsDbContextFactory : IDesignTimeDbContextFactory<ProductsDbContext>
{
    public ProductsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__UnicoreCRM")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=UnicoreCRM_Development;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<ProductsDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "products"))
            .Options;
        return new ProductsDbContext(options);
    }
}
