using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

namespace UnicoreCRM.Platform;

public static class WorkspaceOwnerAuthorityMaintenance
{
    public static async Task RunWorkspaceOwnerAuthorityRepairAsync(
        this IServiceProvider services,
        string? workspaceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (workspaceId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<WorkspaceOwnerAuthorityRepairService>()
            .RunAsync(workspaceId, cancellationToken);
    }
}
