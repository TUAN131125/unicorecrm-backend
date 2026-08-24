using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.Billing.Payments;

internal static class PaymentsModule
{
    internal static IServiceCollection AddPaymentsModule(this IServiceCollection services)
    {
        return services;
    }
}
