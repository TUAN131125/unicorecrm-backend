using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Infrastructure.Persistence;

namespace UnicoreCRM.Crm.Deals;

internal static class DealsModule
{
    internal static IServiceCollection AddDealsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<DealsDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "deals")));
        services.AddScoped<IDealsPersistence, EfDealsPersistence>();
        services.AddScoped<DealAuthorization>();
        services.AddScoped<DealMutationExecution>();
        services.AddScoped<Contracts.IDealSummaryReader, Application.ReadDealSummary.DealSummaryReader>();
        services.AddScoped<Application.ListDeals.Handler>();
        services.AddScoped<Application.GetDeal.Handler>();
        services.AddScoped<Application.GetDealForecastSummary.Handler>();
        services.AddScoped<Application.CreateDeal.Handler>();
        services.AddScoped<Application.ReplaceDealProfile.Handler>();
        services.AddScoped<Application.ChangeDealStage.Handler>();
        services.AddScoped<Application.AssignDealOwner.Handler>();
        services.AddScoped<Application.UpdateDealForecast.Handler>();
        services.AddScoped<Application.UpdateDealNextAction.Handler>();
        services.AddScoped<Application.MarkDealWon.Handler>();
        services.AddScoped<Application.MarkDealLost.Handler>();
        services.AddScoped<Application.ArchiveDeal.Handler>();
        services.AddScoped<Application.ArchiveDealsBatch.Handler>();
        return services;
    }
}
