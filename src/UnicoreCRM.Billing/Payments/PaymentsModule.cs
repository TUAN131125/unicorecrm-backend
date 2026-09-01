using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Billing.Payments.Application.Common;
using UnicoreCRM.Billing.Payments.Infrastructure.Persistence;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Billing.Payments;

internal static class PaymentsModule
{
    internal static IServiceCollection AddPaymentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<PaymentsDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "payments")));
        services.AddScoped<IPaymentsPersistence, EfPaymentsPersistence>();
        services.AddDevelopmentSchemaMigration(
            "payments",
            (provider, cancellationToken) => provider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<PaymentAuthorization>();
        services.AddScoped<Application.ListPaymentPlans.Handler>();
        services.AddScoped<Application.ListPaymentScheduleLines.Handler>();
        services.AddScoped<Application.ListPaymentIntents.Handler>();
        services.AddScoped<Application.GetPaymentIntent.Handler>();
        services.AddScoped<Application.GetPaymentIntentStatus.Handler>();
        services.AddScoped<Application.ListPaymentRecords.Handler>();
        services.AddScoped<Application.GetPaymentRecordDetail.Handler>();
        services.AddScoped<IRecordAccessFactProvider, Application.ProvidePaymentRecordAccessFacts.PaymentRecordAccessFactProvider>();
        return services;
    }
}
