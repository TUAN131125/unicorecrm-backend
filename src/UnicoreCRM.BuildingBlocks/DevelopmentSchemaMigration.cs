using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.BuildingBlocks;

/// <summary>
/// An owner-supplied schema migration callback for an explicit maintenance command.
///
/// This is an owner-neutral technical primitive: it carries no business concept and no persistence
/// of its own. Each owner registers a callback that migrates only the owner's own DbContext, so
/// schema ownership stays exactly where it already is. The composition root decides whether and
/// when to run the registered callbacks; it never touches an owner DbContext itself. Registration
/// is intentionally inert during normal application startup.
/// </summary>
public sealed record DevelopmentSchemaMigration(
    string Owner,
    Func<IServiceProvider, CancellationToken, Task> ApplyAsync);

public static class DevelopmentSchemaMigrationRegistration
{
    /// <summary>
    /// Registers the owner's schema migration operation. Registration is inert on its own:
    /// nothing runs unless the composition root receives an explicit migration command.
    /// </summary>
    public static IServiceCollection AddDevelopmentSchemaMigration(
        this IServiceCollection services,
        string owner,
        Func<IServiceProvider, CancellationToken, Task> applyAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(applyAsync);
        services.AddSingleton(new DevelopmentSchemaMigration(owner, applyAsync));
        return services;
    }
}
