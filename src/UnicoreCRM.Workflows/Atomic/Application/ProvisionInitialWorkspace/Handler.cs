using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Workflows.Atomic.Application.Common;
using UnicoreCRM.Workflows.Atomic.Contracts;

namespace UnicoreCRM.Workflows.Atomic.Application.ProvisionInitialWorkspace;

/// <summary>
/// The multi-owner Initial Workspace Provisioning workflow. It orchestrates approved owner
/// contracts only: IdentityAuth verifies the authenticated principal, Workspace creates the
/// Workspace, the ACTIVE creator membership and the configuration seed, and AccessControl
/// creates the initial access assignment. The workflow owns no foreign DbContext, repository or
/// Infrastructure type and writes no foreign state itself.
///
/// The two owner writes are separate owner-local transactions, so the whole workflow is not one
/// atomic commit. Convergence is used instead: the Workspace step is anchored on an
/// account-scoped uniqueness constraint and the AccessControl step is idempotent, so a retry
/// after a partial failure completes the missing work without creating a second Workspace.
/// </summary>
internal sealed class Handler(
    IAuthenticatedIdentityReferenceLookup identities,
    IInitialWorkspaceProvisioning workspaces,
    IInitialWorkspaceAccessProvisioning access,
    TimeProvider timeProvider)
{
    internal async Task<AtomicWorkflowResult<ProvisionInitialWorkspaceResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var validation = ProvisioningDefaults.Resolve(command.Request, out var name, out var logoText, out var configuration);
        if (validation is not null)
            return AtomicWorkflowResult<ProvisionInitialWorkspaceResponse>.Failure(validation);

        // Fail closed: a structurally valid token is not enough, the account must still be active.
        var identity = await identities.FindActiveAsync(command.AccountId, command.MemberId, cancellationToken);
        if (identity is null)
            return AtomicWorkflowResult<ProvisionInitialWorkspaceResponse>.Failure(AtomicWorkflowErrors.AccessDenied());

        var fingerprint = ProvisioningDefaults.Fingerprint(name, logoText, configuration);
        var provisioning = await workspaces.EnsureInitialWorkspaceAsync(
            new InitialWorkspaceProvisioningRequest(
                identity.AccountId,
                identity.MemberId,
                name,
                logoText,
                configuration,
                command.Metadata.IdempotencyKey,
                fingerprint),
            cancellationToken);

        if (provisioning.Status == InitialWorkspaceProvisioningStatus.RejectedExistingWorkspace)
            return AtomicWorkflowResult<ProvisionInitialWorkspaceResponse>.Failure(AtomicWorkflowErrors.WorkspaceAlreadyProvisioned());

        var workspace = provisioning.Workspace
            ?? throw new InvalidOperationException("Workspace provisioning succeeded without an authoritative Workspace summary.");
        var replayed = provisioning.Status == InitialWorkspaceProvisioningStatus.AlreadyProvisioned;
        if (replayed
            && string.Equals(provisioning.IdempotencyKey, command.Metadata.IdempotencyKey, StringComparison.Ordinal)
            && !string.Equals(provisioning.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return AtomicWorkflowResult<ProvisionInitialWorkspaceResponse>.Failure(
                AtomicWorkflowErrors.IdempotencyReused(command.Metadata.IdempotencyKey));
        }

        // Always re-run the AccessControl participant so an interrupted earlier attempt converges.
        await access.EnsureInitialWorkspaceAccessAsync(workspace.WorkspaceId, workspace.MembershipId, cancellationToken);

        var response = new ProvisionInitialWorkspaceResponse(
            AtomicWorkflowIds.New("command"),
            command.Metadata.CorrelationId,
            replayed ? "REPLAYED" : "PROVISIONED",
            workspace.WorkspaceId,
            workspace.MembershipId,
            workspace,
            provisioning.ProvisionedAt ?? timeProvider.GetUtcNow());
        return AtomicWorkflowResult<ProvisionInitialWorkspaceResponse>.Success(response, replayed ? 200 : 201);
    }
}
