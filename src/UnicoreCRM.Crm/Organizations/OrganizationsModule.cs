using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Infrastructure.Persistence;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Organizations;

internal static class OrganizationsModule
{
    internal static IServiceCollection AddOrganizationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<OrganizationsDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "organizations")));
        services.AddScoped<IOrganizationsPersistence, EfOrganizationsPersistence>();
        services.AddDevelopmentSchemaMigration(
            "organizations",
            (provider, cancellationToken) => provider.GetRequiredService<OrganizationsDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<OrganizationAuthorization>();
        services.AddScoped<Application.ListOrganizations.Handler>();
        services.AddScoped<Application.GetOrganization.Handler>();
        services.AddScoped<IRecordAccessFactProvider, Application.ProvideOrganizationRecordAccessFacts.OrganizationRecordAccessFactProvider>();
        return services;
    }
}
