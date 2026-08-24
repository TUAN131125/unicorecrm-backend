using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Workflows.Durable.Application.Common;
using UnicoreCRM.Workflows.Durable.Contracts;

namespace UnicoreCRM.Workflows.Durable.Application.ProvisionInitialWorkspace;

/// <summary>
/// The multi-owner Initial Workspace Provisioning workflow. It orchestrates approved owner
/// contracts only: IdentityAuth verifies the authenticated principal, Workspace creates the
/// Workspace, the ACTIVE creator membership and the configuration seed, and AccessControl creates
/// the initial access assignment. The workflow owns no foreign DbContext, repository or
/// Infrastructure type and writes no foreign state itself.
///
/// It is a durable workflow, not an atomic one. The two owner writes are separate owner-local
/// transactions and cannot commit or roll back together, so completion is durable progress rather
/// than a single commit: the Workspace step commits an <c>AccessPending</c> anchor, and the anchor
/// is advanced to completed only after the AccessControl participant commits. An attempt that
/// stops in between leaves authoritative outstanding work that both this request path and
/// <see cref="InitialWorkspaceProvisioningResumeService"/> converge on, without ever creating a
/// second Workspace.
/// </summary>
internal sealed class Handler(
    IAuthenticatedIdentityReferenceLookup identities,
    IInitialWorkspaceProvisioning workspaces,
    InitialWorkspaceAccessCompletion completion,
    TimeProvider timeProvider)
{
    internal async Task<DurableWorkflowResult<ProvisionInitialWorkspaceResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var validation = ProvisioningDefaults.Resolve(command.Request, out var name, out var logoText, out var configuration);
        if (validation is not null)
            return DurableWorkflowResult<ProvisionInitialWorkspaceResponse>.Failure(validation);

        // Fail closed: a structurally valid token is not enough, the account must still be active.
        var identity = await identities.FindActiveAsync(command.AccountId, command.MemberId, cancellationToken);
        if (identity is null)
            return DurableWorkflowResult<ProvisionInitialWorkspaceResponse>.Failure(DurableWorkflowErrors.AccessDenied());

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

        // The account-scoped lifecycle decision precedes idempotency comparison: an account whose
        // Workspace access did not come from initial provisioning has no anchor to compare against.
        if (provisioning.Status == InitialWorkspaceProvisioningStatus.RejectedExistingWorkspace)
            return DurableWorkflowResult<ProvisionInitialWorkspaceResponse>.Failure(DurableWorkflowErrors.WorkspaceAlreadyProvisioned());

        var workspace = provisioning.Workspace
            ?? throw new InvalidOperationException("Workspace provisioning succeeded without an authoritative Workspace summary.");
        var replayed = provisioning.Status == InitialWorkspaceProvisioningStatus.AlreadyProvisioned;
        if (replayed
            && string.Equals(provisioning.IdempotencyKey, command.Metadata.IdempotencyKey, StringComparison.Ordinal)
            && !string.Equals(provisioning.RequestFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return DurableWorkflowResult<ProvisionInitialWorkspaceResponse>.Failure(
                DurableWorkflowErrors.IdempotencyReused(command.Metadata.IdempotencyKey));
        }

        // Outstanding work only. A completed anchor needs no further AccessControl write, and a
        // replay never rewrites the stored provisioning values.
        if (provisioning.AccessPending)
            await completion.CompleteAsync(identity.AccountId, workspace.WorkspaceId, workspace.MembershipId, cancellationToken);

        var response = new ProvisionInitialWorkspaceResponse(
            DurableWorkflowIds.New("command"),
            command.Metadata.CorrelationId,
            replayed ? "REPLAYED" : "PROVISIONED",
            workspace.WorkspaceId,
            workspace.MembershipId,
            workspace,
            provisioning.ProvisionedAt ?? timeProvider.GetUtcNow());
        return DurableWorkflowResult<ProvisionInitialWorkspaceResponse>.Success(response, replayed ? 200 : 201);
    }
}
