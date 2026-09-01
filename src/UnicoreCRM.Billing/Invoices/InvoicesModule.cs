using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Billing.Invoices.Application.Common;
using UnicoreCRM.Billing.Invoices.Infrastructure.Persistence;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Billing.Invoices;

internal static class InvoicesModule
{
    internal static IServiceCollection AddInvoicesModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<InvoicesDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "invoices")));
        services.AddScoped<IInvoicesPersistence, EfInvoicesPersistence>();
        services.AddDevelopmentSchemaMigration(
            "invoices",
            (provider, cancellationToken) => provider.GetRequiredService<InvoicesDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<InvoiceAuthorization>();
        services.AddScoped<Application.ListInvoices.Handler>();
        services.AddScoped<Application.GetInvoice.Handler>();
        services.AddScoped<IRecordAccessFactProvider, Application.ProvideInvoiceRecordAccessFacts.InvoiceRecordAccessFactProvider>();
        return services;
    }
}
