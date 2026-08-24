using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddScoped<CurrentAuthorizationContextAccessor>();
        services.AddScoped<ICurrentAuthorizationContext>(provider => provider.GetRequiredService<CurrentAuthorizationContextAccessor>());
        services.AddScoped<IResolvedAuthorizationContextSetter>(provider => provider.GetRequiredService<CurrentAuthorizationContextAccessor>());
        services.AddScoped<Application.ProvisionInitialWorkspaceAccess.IInitialWorkspaceAccessPersistence, EfInitialWorkspaceAccessPersistence>();
        services.AddScoped<IInitialWorkspaceAccessProvisioning, Application.ProvisionInitialWorkspaceAccess.InitialWorkspaceAccessProvisioningService>();
        services.AddScoped<IAccessAuthorizer, AccessAuthorizer>();
        services.AddScoped<IDelegatedAccessAuthorizer, AccessAuthorizer>();
        services.AddScoped<Application.GetCurrentAuthorizationContext.Handler>();
        services.AddHostedService<DevelopmentAccessControlBootstrap>();
        return services;
    }
}
