using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Billing.Invoices;
using UnicoreCRM.Billing.Payments;

namespace UnicoreCRM.Billing;

public static class BillingModule
{
    public static IServiceCollection AddBillingModule(this IServiceCollection services)
    {
        services.AddInvoicesModule();
        services.AddPaymentsModule();

        return services;
    }
}
