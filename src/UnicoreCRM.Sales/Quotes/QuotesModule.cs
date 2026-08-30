using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

namespace UnicoreCRM.Sales.Quotes;

internal static class QuotesModule
{
    internal static IServiceCollection AddQuotesModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<QuotesDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "quotes")));
        services.AddScoped<IQuotesPersistence, EfQuotesPersistence>();
        services.AddDevelopmentSchemaMigration(
            "quotes",
            (provider, cancellationToken) => provider.GetRequiredService<QuotesDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<QuoteAuthorization>();
        services.AddScoped<Application.ListQuotes.Handler>();
        services.AddScoped<Application.GetQuote.Handler>();
        services.AddScoped<IRecordAccessFactProvider, Application.ProvideQuoteRecordAccessFacts.QuoteRecordAccessFactProvider>();
        return services;
    }
}
