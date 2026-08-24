using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Sales.Products;
using UnicoreCRM.Sales.Quotes;
using UnicoreCRM.Sales.Orders;

namespace UnicoreCRM.Sales;

public static class SalesModule
{
    public static IServiceCollection AddSalesModule(this IServiceCollection services)
    {
        services.AddProductsModule();
        services.AddQuotesModule();
        services.AddOrdersModule();

        return services;
    }
}
