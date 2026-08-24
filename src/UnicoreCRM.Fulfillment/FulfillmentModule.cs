using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Fulfillment.Shipping;
using UnicoreCRM.Fulfillment.Returns;

namespace UnicoreCRM.Fulfillment;

public static class FulfillmentModule
{
    public static IServiceCollection AddFulfillmentModule(this IServiceCollection services)
    {
        services.AddShippingModule();
        services.AddReturnsModule();

        return services;
    }
}
