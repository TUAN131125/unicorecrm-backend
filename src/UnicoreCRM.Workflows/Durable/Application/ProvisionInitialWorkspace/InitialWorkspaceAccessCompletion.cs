using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Workflows.Durable.Infrastructure;

namespace UnicoreCRM.Workflows.Durable.Application.ProvisionInitialWorkspace;

/// <summary>
/// The second durable step of Initial Workspace Provisioning: create the AccessControl assignment
/// for the creator, then advance the Workspace-owned anchor to completed. It is convergent, so the
/// request path and the background resume path can both run it safely, and running it against an
/// already-assigned Workspace changes nothing.
/// </summary>
internal sealed class InitialWorkspaceAccessCompletion(
    IInitialWorkspaceAccessProvisioning access,
    IInitialWorkspaceProvisioning workspaces,
    IHostEnvironment environment,
    IOptions<DurableWorkflowOptions> options)
{
    internal async Task CompleteAsync(
        string accountId,
        string workspaceId,
        string membershipId,
        CancellationToken cancellationToken)
    {
        var faults = options.Value.InitialWorkspaceProvisioning.DevelopmentFaultInjection;
        if (environment.IsDevelopment() && faults.FailAccessAssignment)
            throw new InvalidOperationException("Development fault injection failed the initial Workspace access assignment.");

        await access.EnsureInitialWorkspaceAccessAsync(workspaceId, membershipId, cancellationToken);
        await workspaces.CompleteInitialWorkspaceAsync(accountId, cancellationToken);
    }
}
