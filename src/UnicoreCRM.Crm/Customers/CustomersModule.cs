using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Infrastructure.Persistence;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Customers;

internal static class CustomersModule
{
    internal static IServiceCollection AddCustomersModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<CustomersDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "customers")));
        services.AddScoped<ICustomersPersistence, EfCustomersPersistence>();
        services.AddDevelopmentSchemaMigration(
            "customers",
            (provider, cancellationToken) => provider.GetRequiredService<CustomersDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<CustomerAuthorization>();
        services.AddScoped<Application.ListCustomers.Handler>();
        services.AddScoped<Application.GetCustomer.Handler>();
        services.AddScoped<IRecordAccessFactProvider, Application.ProvideCustomerRecordAccessFacts.CustomerRecordAccessFactProvider>();
        return services;
    }
}
