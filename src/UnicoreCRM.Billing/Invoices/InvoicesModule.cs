using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.Billing.Invoices;

internal static class InvoicesModule
{
    internal static IServiceCollection AddInvoicesModule(this IServiceCollection services)
    {
        return services;
    }
}
