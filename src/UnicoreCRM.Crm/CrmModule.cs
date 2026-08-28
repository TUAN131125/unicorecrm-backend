using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using UnicoreCRM.Crm.Leads;
using UnicoreCRM.Crm.Deals;
using UnicoreCRM.Crm.Contacts;
using UnicoreCRM.Crm.Customers;
using UnicoreCRM.Crm.Organizations;

namespace UnicoreCRM.Crm;

public static class CrmModule
{
    public static IServiceCollection AddCrmModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLeadsModule(configuration);
        services.AddDealsModule(configuration);
        services.AddContactsModule(configuration);
        services.AddCustomersModule(configuration);
        services.AddOrganizationsModule(configuration);

        return services;
    }
}
