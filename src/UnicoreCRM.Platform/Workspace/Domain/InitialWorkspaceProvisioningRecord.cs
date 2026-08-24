namespace UnicoreCRM.Platform.Workspace.Domain;

/// <summary>
/// The Workspace-owned durable anchor for Initial Workspace Provisioning of one authenticated
/// account. Its account-scoped primary key is the concurrency and idempotency authority: at most
/// one initial Workspace can ever exist per account, so retries and concurrent double submits
/// converge on the same Workspace instead of creating another.
///
/// The anchor also carries durable progress. Provisioning completes across two owner-local
/// transactions, so the record is committed as <see cref="InitialWorkspaceProvisioningState.AccessPending"/>
/// together with the Workspace and is advanced to <see cref="InitialWorkspaceProvisioningState.Completed"/>
/// only after the AccessControl participant has committed the creator assignment. An anchor left
/// in <c>AccessPending</c> is the authoritative outstanding-work signal that drives recovery.
/// </summary>
internal sealed class InitialWorkspaceProvisioningRecord
{
    private InitialWorkspaceProvisioningRecord() { }

    internal InitialWorkspaceProvisioningRecord(
        string accountId,
        string memberId,
        string workspaceId,
        string membershipId,
        string idempotencyKey,
        string requestFingerprint,
        DateTimeOffset now)
    {
        AccountId = accountId;
        MemberId = memberId;
        WorkspaceId = workspaceId;
        MembershipId = membershipId;
        IdempotencyKey = idempotencyKey;
        RequestFingerprint = requestFingerprint;
        State = InitialWorkspaceProvisioningState.AccessPending;
        ProvisionedAt = now;
    }

    public string AccountId { get; private set; } = null!;
    public string MemberId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string MembershipId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string RequestFingerprint { get; private set; } = null!;
    public InitialWorkspaceProvisioningState State { get; private set; }
    public DateTimeOffset ProvisionedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Idempotent completion. Re-running a completed anchor changes nothing.</summary>
    internal void MarkCompleted(DateTimeOffset now)
    {
        if (State == InitialWorkspaceProvisioningState.Completed)
            return;
        State = InitialWorkspaceProvisioningState.Completed;
        CompletedAt = now;
    }
}

internal enum InitialWorkspaceProvisioningState
{
    /// <summary>The Workspace, membership and configuration seed exist; the access assignment is outstanding.</summary>
    AccessPending,

    /// <summary>Every participant committed. The Workspace is fully usable.</summary>
    Completed
}
