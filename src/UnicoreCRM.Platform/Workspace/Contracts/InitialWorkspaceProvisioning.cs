namespace UnicoreCRM.Platform.Workspace.Contracts;

/// <summary>
/// The narrow Workspace participant boundary for the multi-owner Initial Workspace
/// Provisioning workflow. Workspace remains the sole authority for Workspace identity,
/// membership validity and the Workspace-owned bootstrap read projection. The caller
/// supplies validated business values only; every aggregate identifier, the Workspace key
/// and the ACTIVE creator membership are assigned by this owner.
/// </summary>
public interface IInitialWorkspaceProvisioning
{
    Task<InitialWorkspaceProvisioningResult> EnsureInitialWorkspaceAsync(
        InitialWorkspaceProvisioningRequest request,
        CancellationToken cancellationToken);
}

/// <param name="AccountId">The authenticated account verified by IdentityAuth.</param>
/// <param name="MemberId">The authenticated global member reference verified by IdentityAuth.</param>
/// <param name="Configuration">
/// The initial configuration seed for the Workspace-owned bootstrap read projection.
/// </param>
/// <param name="IdempotencyKey">Provisioning-execution evidence retained on the account anchor.</param>
/// <param name="RequestFingerprint">Hash of the effective provisioning values, retained for replay comparison.</param>
public sealed record InitialWorkspaceProvisioningRequest(
    string AccountId,
    string MemberId,
    string Name,
    string LogoText,
    InitialWorkspaceConfigurationSeed Configuration,
    string IdempotencyKey,
    string RequestFingerprint);

/// <summary>
/// The minimal admitted provisioning configuration contract. It carries only the values the
/// current Workspace bootstrap read contract requires at creation time. It is a creation-time
/// seed and never a configuration mutation surface; the deferred WorkspaceConfig owner remains
/// the future authority for configuration change.
/// </summary>
public sealed record InitialWorkspaceConfigurationSeed(
    string Locale,
    string TimeZone,
    string BaseCurrency,
    IReadOnlyList<string> EnabledModuleKeys,
    IReadOnlyList<string> AvailableProductSpaces);

public enum InitialWorkspaceProvisioningStatus
{
    /// <summary>This call created the Workspace, the ACTIVE creator membership and the configuration seed.</summary>
    Provisioned,

    /// <summary>An initial Workspace already exists for the account; the same authoritative result is returned.</summary>
    AlreadyProvisioned,

    /// <summary>The account already holds Workspace access that initial provisioning did not create.</summary>
    RejectedExistingWorkspace
}

public sealed record InitialWorkspaceProvisioningResult(
    InitialWorkspaceProvisioningStatus Status,
    WorkspaceMembershipSummary? Workspace,
    DateTimeOffset? ProvisionedAt,
    string? IdempotencyKey = null,
    string? RequestFingerprint = null);
