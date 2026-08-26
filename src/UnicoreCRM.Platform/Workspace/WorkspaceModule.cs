using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Platform.Workspace.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Infrastructure;
using UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

namespace UnicoreCRM.Platform.Workspace;

internal static class WorkspaceModule
{
    internal static IServiceCollection AddWorkspaceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WorkspaceOptions>()
            .Bind(configuration.GetSection(WorkspaceOptions.SectionName));

        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<WorkspaceDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "workspace")));
        services.AddScoped<IWorkspacePersistence, EfWorkspacePersistence>();
        services.AddDevelopmentSchemaMigration(
            "workspace",
            (provider, cancellationToken) => provider.GetRequiredService<WorkspaceDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<IDevelopmentWorkspaceReferenceLookup, EfDevelopmentWorkspaceReferenceLookup>();
        services.AddScoped<IWorkspaceMemberReferenceValidator, EfWorkspaceMemberReferenceValidator>();
        services.AddScoped<IWorkspaceCurrencyConfigurationReader, EfWorkspaceCurrencyConfigurationReader>();
        services.AddScoped<ITrustedWorkspaceMemberResolver, EfWorkspaceMemberReferenceValidator>();
        services.AddScoped<IWorkspaceContextResolver, WorkspaceContextResolver>();
        services.AddScoped<CurrentWorkspaceAccessor>();
        services.AddScoped<ICurrentWorkspace>(provider => provider.GetRequiredService<CurrentWorkspaceAccessor>());
        services.AddScoped<ITrustedWorkspaceSetter>(provider => provider.GetRequiredService<CurrentWorkspaceAccessor>());
        services.AddScoped<Application.ProvisionInitialWorkspace.IInitialWorkspaceProvisioningPersistence, EfInitialWorkspaceProvisioningPersistence>();
        services.AddScoped<IInitialWorkspaceProvisioning, Application.ProvisionInitialWorkspace.InitialWorkspaceProvisioningService>();
        services.AddScoped<Application.ListMyWorkspaces.Handler>();
        services.AddScoped<Application.GetWorkspaceBootstrap.Handler>();
        services.AddHostedService<DevelopmentWorkspaceBootstrap>();
        return services;
    }
}
