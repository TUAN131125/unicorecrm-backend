using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using UnicoreCRM.Sales.Products;
using UnicoreCRM.Sales.Quotes;
using UnicoreCRM.Sales.Orders;

namespace UnicoreCRM.Sales;

public static class SalesModule
{
    public static IServiceCollection AddSalesModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddProductsModule(configuration);
        services.AddQuotesModule(configuration);
        services.AddOrdersModule();

        return services;
    }
}
