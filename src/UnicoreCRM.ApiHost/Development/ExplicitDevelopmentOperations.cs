using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UnicoreCRM.BuildingBlocks;

namespace UnicoreCRM.ApiHost.Development;

/// <summary>
/// Runs owner-provided maintenance operations only for an explicit ApiHost command. These methods
/// are never registered as hosted services, so constructing or starting the HTTP host cannot invoke
/// schema migration or Development fixture mutation.
/// </summary>
internal static class ExplicitDevelopmentOperations
{
    internal static async Task RunOwnerSchemaMigrationsAsync(
        this IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var migration in services.GetServices<DevelopmentSchemaMigration>())
        {
            await using var scope = services.CreateAsyncScope();
            await migration.ApplyAsync(scope.ServiceProvider, cancellationToken);
            logger.LogInformation("Schema migration applied for {Owner}.", migration.Owner);
        }
    }

    internal static async Task RunDevelopmentBootstrapAsync(
        this IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("The explicit demo bootstrap command is available only in the Development environment.");

        foreach (var action in services.GetServices<DevelopmentBootstrapAction>())
        {
            await using var scope = services.CreateAsyncScope();
            await action.ApplyAsync(scope.ServiceProvider, cancellationToken);
            logger.LogInformation("Development bootstrap action completed for {Owner}.", action.Owner);
        }
    }
}
