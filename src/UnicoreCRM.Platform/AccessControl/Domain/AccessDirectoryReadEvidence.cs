namespace UnicoreCRM.Platform.AccessControl.Domain;

internal sealed class AccessDirectoryReadEvidence
{
    private AccessDirectoryReadEvidence() { }

    internal AccessDirectoryReadEvidence(
        string workspaceId,
        string actorAccountId,
        string actorMembershipId,
        string actorMemberId,
        string requestId,
        string correlationId,
        DateTimeOffset occurredAt)
    {
        EvidenceId = AccessControlIds.New("audit");
        OperationId = "getWorkspaceAccessDirectory";
        WorkspaceId = workspaceId;
        ActorAccountId = actorAccountId;
        ActorMembershipId = actorMembershipId;
        ActorMemberId = actorMemberId;
        RequestId = requestId;
        CorrelationId = correlationId;
        Outcome = "READ";
        OccurredAt = occurredAt;
    }

    public string EvidenceId { get; private set; } = null!;
    public string OperationId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string ActorAccountId { get; private set; } = null!;
    public string ActorMembershipId { get; private set; } = null!;
    public string ActorMemberId { get; private set; } = null!;
    public string RequestId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
}
