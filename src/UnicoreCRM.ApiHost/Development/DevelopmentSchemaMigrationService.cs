using UnicoreCRM.BuildingBlocks;

namespace UnicoreCRM.ApiHost.Development;

/// <summary>
/// Applies the owner-registered Development schema migrations before any Development seed runs.
///
/// ApiHost owns no persistence: it holds no DbContext, no repository and no Infrastructure type. It
/// only invokes the callbacks each owner registered for its own schema, in registration order, so
/// schema ownership stays with the owner. The pass runs only in the Development environment and
/// only when <c>Development:ApplyMigrations</c> is enabled, so a non-Development host never
/// migrates a database implicitly.
/// </summary>
internal sealed class DevelopmentSchemaMigrationService(
    IHostEnvironment environment,
    IConfiguration configuration,
    IServiceScopeFactory scopeFactory,
    IEnumerable<DevelopmentSchemaMigration> migrations,
    ILogger<DevelopmentSchemaMigrationService> logger) : IHostedService
{
    internal const string EnabledKey = "Development:ApplyMigrations";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() || !configuration.GetValue(EnabledKey, false))
            return;

        foreach (var migration in migrations)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await migration.ApplyAsync(scope.ServiceProvider, cancellationToken);
            logger.LogInformation("Development schema migration applied for {Owner}.", migration.Owner);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
