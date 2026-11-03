using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Orders.Application.Common;
using UnicoreCRM.Sales.Orders.Infrastructure.Persistence;

namespace UnicoreCRM.Sales.Orders;

internal static class OrdersModule
{
    internal static IServiceCollection AddOrdersModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddScoped<UnicoreCRM.Sales.Quotes.Contracts.IQuoteRetentionVerifier, OrdersQuoteRetentionVerifier>();
        services.AddDbContext<OrdersDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "orders")));
        services.AddScoped<IOrdersPersistence, EfOrdersPersistence>();
        services.AddScoped<Contracts.IAcceptedQuoteOrderWriter,AcceptedQuoteOrderWriter>();
        services.AddScoped<Contracts.IOrderConfirmationParticipant,OrderConfirmationParticipant>();
        services.AddScoped<Contracts.IOrderPaymentPlanSourceReader,OrderPaymentPlanSourceReader>();
        services.AddScoped<Contracts.IConfirmedQuoteCustomerSourceReader,ConfirmedQuoteCustomerSourceReader>();
        services.AddScoped<Contracts.IDealOrderCommitmentVerifier, DealOrderCommitmentVerifier>();
        services.AddDevelopmentSchemaMigration(
            "orders",
            (provider, cancellationToken) => provider.GetRequiredService<OrdersDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<OrderAuthorization>();
        services.AddScoped<Application.ListOrders.Handler>();
        services.AddScoped<Application.GetOrder.Handler>();
        services.AddScoped<IRecordAccessFactProvider, Application.ProvideOrderRecordAccessFacts.OrderRecordAccessFactProvider>();
        return services;
    }
}
