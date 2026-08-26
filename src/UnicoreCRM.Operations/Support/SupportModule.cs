using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Operations.Support.Infrastructure.Persistence;

namespace UnicoreCRM.Operations.Support;

internal static class SupportModule
{
    internal static IServiceCollection AddSupportModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<SupportDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "support")));
        services.AddScoped<ISupportPersistence, EfSupportPersistence>();
        services.AddDevelopmentSchemaMigration(
            "support",
            (provider, cancellationToken) => provider.GetRequiredService<SupportDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<SupportAuthorization>();
        services.AddScoped<SupportMutationExecution>();
        services.AddScoped<Application.ListSupportCases.Handler>();
        services.AddScoped<Application.GetSupportCase.Handler>();
        services.AddScoped<Application.CreateSupportCase.Handler>();
        services.AddScoped<Application.ReplaceSupportCaseProfile.Handler>();
        services.AddScoped<Application.AssignSupportCase.Handler>();
        services.AddScoped<Application.TransitionSupportCase.Handler>();
        services.AddScoped<Application.AddSupportCaseReply.Handler>();
        services.AddScoped<Application.AddSupportCaseInternalNote.Handler>();
        return services;
    }
}
