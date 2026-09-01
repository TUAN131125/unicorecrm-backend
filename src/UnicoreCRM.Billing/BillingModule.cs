using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Billing.Invoices;
using UnicoreCRM.Billing.Payments;

namespace UnicoreCRM.Billing;

public static class BillingModule
{
    public static IServiceCollection AddBillingModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddInvoicesModule(configuration);
        services.AddPaymentsModule(configuration);

        return services;
    }
}
