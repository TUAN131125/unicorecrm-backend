namespace UnicoreCRM.Platform.Workspace.Domain;

/// <summary>
/// The Workspace-owned durable anchor proving that Initial Workspace Provisioning already ran
/// for one authenticated account. Its account-scoped primary key is the concurrency and
/// idempotency authority: at most one initial Workspace can ever exist per account, so retries
/// and concurrent double submits converge on the same Workspace instead of creating another.
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
        ProvisionedAt = now;
    }

    public string AccountId { get; private set; } = null!;
    public string MemberId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string MembershipId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string RequestFingerprint { get; private set; } = null!;
    public DateTimeOffset ProvisionedAt { get; private set; }
}
