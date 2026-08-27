using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Infrastructure;
using UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

namespace UnicoreCRM.Platform.AccessControl;

internal static class AccessControlModule
{
    internal static IServiceCollection AddAccessControlModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccessControlOptions>()
            .Bind(configuration.GetSection(AccessControlOptions.SectionName));

        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<AccessControlDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "access")));
        services.AddScoped<IAccessControlPersistence, EfAccessControlPersistence>();
        services.AddDevelopmentSchemaMigration(
            "access-control",
            (provider, cancellationToken) => provider.GetRequiredService<AccessControlDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<CurrentAuthorizationContextAccessor>();
        services.AddScoped<ICurrentAuthorizationContext>(provider => provider.GetRequiredService<CurrentAuthorizationContextAccessor>());
        services.AddScoped<IResolvedAuthorizationContextSetter>(provider => provider.GetRequiredService<CurrentAuthorizationContextAccessor>());
        services.AddScoped<Application.ProvisionInitialWorkspaceAccess.IInitialWorkspaceAccessPersistence, EfInitialWorkspaceAccessPersistence>();
        services.AddScoped<IInitialWorkspaceAccessProvisioning, Application.ProvisionInitialWorkspaceAccess.InitialWorkspaceAccessProvisioningService>();
        services.AddScoped<IAccessAuthorizer, AccessAuthorizer>();
        services.AddScoped<IDelegatedAccessAuthorizer, AccessAuthorizer>();
        services.AddScoped<IAccessContextAuthorizer, AccessAuthorizer>();
        services.AddScoped<Application.GetCurrentAuthorizationContext.Handler>();
        // Owner fact providers are registered by their own modules. The registry is scoped so a
        // provider that needs its owner's scoped persistence can be resolved normally, and it
        // rejects two owners claiming the same resource key at composition time.
        services.AddScoped<RecordAccessFactProviderRegistry>();
        services.AddScoped<RecordAccessEvaluator>();
        services.AddScoped<IRecordAccessEvaluator>(provider => provider.GetRequiredService<RecordAccessEvaluator>());
        services.AddScoped<Application.EvaluateEffectiveRecordAccess.Handler>();
        services.AddHostedService<DevelopmentAccessControlBootstrap>();
        return services;
    }
}
