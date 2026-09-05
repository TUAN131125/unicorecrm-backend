using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;

namespace UnicoreCRM.Workflows.Atomic;

internal static class AtomicModule
{
    internal static IServiceCollection AddAtomicModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<WorkflowsDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "workflow")));
        services.AddDevelopmentSchemaMigration(
            "workflow",
            (provider, cancellationToken) => provider.GetRequiredService<WorkflowsDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<Contracts.ILeadNurtureQualificationWorkflow,
            Application.QualifyLeadForNurture.Handler>();
        services.AddScoped<Contracts.ILeadOpportunityQualificationWorkflow,
            Application.QualifyLeadForOpportunity.Handler>();
        return services;
    }
}
