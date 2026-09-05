using Microsoft.Extensions.DependencyInjection;

namespace UnicoreCRM.BuildingBlocks;

/// <summary>
/// An owner-supplied Development fixture action. Registration is inert; ApiHost invokes these
/// actions only for its explicit Development bootstrap command.
/// </summary>
public sealed record DevelopmentBootstrapAction(
    string Owner,
    Func<IServiceProvider, CancellationToken, Task> ApplyAsync);

public static class DevelopmentBootstrapActionRegistration
{
    public static IServiceCollection AddDevelopmentBootstrapAction(
        this IServiceCollection services,
        string owner,
        Func<IServiceProvider, CancellationToken, Task> applyAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(applyAsync);
        services.AddSingleton(new DevelopmentBootstrapAction(owner, applyAsync));
        return services;
    }
}
