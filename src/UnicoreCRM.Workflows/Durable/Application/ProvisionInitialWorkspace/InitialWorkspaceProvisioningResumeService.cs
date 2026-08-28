using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Workflows.Durable.Infrastructure;

namespace UnicoreCRM.Workflows.Durable.Application.ProvisionInitialWorkspace;

/// <summary>
/// The durable resume path for Initial Workspace Provisioning.
///
/// Provisioning completes across two owner-local transactions, so an attempt can commit the
/// Workspace, the ACTIVE creator membership and the configuration seed and then fail before the
/// AccessControl assignment. Without recovery that account would list one active membership,
/// never re-enter Initial Setup, and never pass Workspace bootstrap. This service closes that
/// window: it reads the Workspace-owned outstanding-work anchors and finishes them.
///
/// It runs once at startup and then on a server-owned interval, so recovery does not depend on the
/// client retrying, on a login event, on a first-login flag or on any client-held state.
/// <c>listMyWorkspaces</c> remains the only lifecycle authority and is not consulted or changed.
/// </summary>
internal sealed class InitialWorkspaceProvisioningResumeService(
    IServiceScopeFactory scopeFactory,
    IOptions<DurableWorkflowOptions> options,
    ILogger<InitialWorkspaceProvisioningResumeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.InitialWorkspaceProvisioning;
        if (!settings.ResumeEnabled)
            return;
        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.ResumeIntervalSeconds, 1, 3600));
        var batchSize = Math.Clamp(settings.ResumeBatchSize, 1, 500);

        try
        {
            await ConvergeExistingAccessDefinitionsAsync(batchSize, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Initial Workspace access policy convergence scan failed.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ResumeAsync(batchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A resume pass must never take the host down; the anchor stays outstanding.
                logger.LogError(exception, "Initial Workspace provisioning resume pass failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConvergeExistingAccessDefinitionsAsync(int batchSize, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var workspaces = scope.ServiceProvider.GetRequiredService<IInitialWorkspaceProvisioning>();
            var anchors = await workspaces.ListAccessConvergenceAnchorsAsync(offset, batchSize, cancellationToken);
            if (anchors.Count == 0)
                return;

            foreach (var anchor in anchors)
            {
                await using var itemScope = scopeFactory.CreateAsyncScope();
                var access = itemScope.ServiceProvider.GetRequiredService<IInitialWorkspaceAccessProvisioning>();
                try
                {
                    await access.EnsureInitialWorkspaceAccessAsync(
                        anchor.WorkspaceId,
                        anchor.MembershipId,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Initial Workspace access policy could not converge workspace {WorkspaceId}; its role was left unchanged.",
                        anchor.WorkspaceId);
                }
            }

            offset += anchors.Count;
        }
    }

    private async Task ResumeAsync(int batchSize, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var workspaces = scope.ServiceProvider.GetRequiredService<IInitialWorkspaceProvisioning>();
        var pending = await workspaces.ListAccessPendingAsync(batchSize, cancellationToken);
        if (pending.Count == 0)
            return;

        foreach (var item in pending)
        {
            // Each anchor gets its own scope so one failure cannot poison the others.
            await using var itemScope = scopeFactory.CreateAsyncScope();
            var completion = itemScope.ServiceProvider.GetRequiredService<InitialWorkspaceAccessCompletion>();
            try
            {
                await completion.CompleteAsync(item.AccountId, item.WorkspaceId, item.MembershipId, cancellationToken);
                logger.LogInformation(
                    "Initial Workspace provisioning resumed and completed workspace {WorkspaceId} for membership {MembershipId}.",
                    item.WorkspaceId,
                    item.MembershipId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Initial Workspace provisioning could not resume workspace {WorkspaceId}; it remains outstanding.",
                    item.WorkspaceId);
            }
        }
    }
}
