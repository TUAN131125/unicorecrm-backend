using UnicoreCRM.Platform.Workspace.Application.Common;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Application.ProvisionInitialWorkspace;

/// <summary>
/// The owner-local persistence surface used only by Initial Workspace Provisioning. It is kept
/// separate from the Workspace read persistence so the read contracts gain no mutation surface.
/// </summary>
internal interface IInitialWorkspaceProvisioningPersistence
{
    Task<InitialWorkspaceProvisioningRecord?> FindProvisioningRecordAsync(string accountId, CancellationToken cancellationToken);
    Task<bool> HasActiveMembershipAsync(string accountId, CancellationToken cancellationToken);
    Task<bool> WorkspaceKeyExistsAsync(string workspaceKey, CancellationToken cancellationToken);
    Task<WorkspaceMembershipReadModel?> FindMembershipAsync(string workspaceId, string membershipId, CancellationToken cancellationToken);

    /// <summary>
    /// Commits the Workspace, the ACTIVE creator membership, the configuration seed and the
    /// account-scoped provisioning record inside one owner-local transaction. It returns
    /// <c>false</c> when the account-scoped uniqueness constraint rejected the write, which is
    /// the concurrent double-submit signal, and leaves no partial state behind.
    /// </summary>
    Task<bool> TryCommitProvisioningAsync(
        WorkspaceDefinition workspace,
        WorkspaceMembership membership,
        WorkspaceBootstrapProjection configuration,
        InitialWorkspaceProvisioningRecord provisioning,
        CancellationToken cancellationToken);
}
