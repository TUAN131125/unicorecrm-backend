using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Products.Infrastructure.Persistence;

namespace UnicoreCRM.Sales.Products;

internal static class ProductsModule
{
    internal static IServiceCollection AddProductsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<ProductsDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "products")));
        services.AddScoped<IProductsPersistence, EfProductsPersistence>();
        services.AddDevelopmentSchemaMigration(
            "products",
            (provider, cancellationToken) => provider.GetRequiredService<ProductsDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<ProductAuthorization>();
        services.AddScoped<ProductMutationExecution>();
        services.AddScoped<ProductBatchMutationExecution>();
        services.AddScoped<Application.ListProducts.Handler>();
        services.AddScoped<Application.GetProduct.Handler>();
        services.AddScoped<Application.GetProductAvailability.Handler>();
        services.AddScoped<Application.GetProductPriceProjection.Handler>();
        services.AddScoped<Application.CreateProduct.Handler>();
        services.AddScoped<Application.ReplaceProduct.Handler>();
        services.AddScoped<Application.ArchiveProduct.Handler>();
        services.AddScoped<Application.RestoreProduct.Handler>();
        services.AddScoped<Application.ArchiveProductsBatch.Handler>();
        services.AddScoped<Application.RestoreProductsBatch.Handler>();
        // The owner publishes its own record-access facts to AccessControl, which never reaches into
        // this module's DbContext.
        services.AddScoped<IRecordAccessFactProvider, Application.ProvideProductRecordAccessFacts.ProductRecordAccessFactProvider>();
        return services;
    }
}
