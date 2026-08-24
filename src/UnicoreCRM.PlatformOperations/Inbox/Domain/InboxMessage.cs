namespace UnicoreCRM.PlatformOperations.Inbox.Domain;

internal sealed class InboxMessage
{
    private InboxMessage() { }

    internal InboxMessage(
        string integrationId,
        string deliveryId,
        string payloadHash,
        string providerCode,
        string workspaceId,
        string delegatedMemberId,
        string correlationId,
        DateTimeOffset receivedAt)
    {
        InboxMessageId = $"inbox_{Guid.NewGuid():N}";
        IntegrationId = integrationId;
        DeliveryId = deliveryId;
        PayloadHash = payloadHash;
        ProviderCode = providerCode;
        WorkspaceId = workspaceId;
        DelegatedMemberId = delegatedMemberId;
        CorrelationId = correlationId;
        Status = InboxStatus.Received;
        AttemptCount = 1;
        ReceivedAt = receivedAt;
        UpdatedAt = receivedAt;
    }

    public string InboxMessageId { get; private set; } = null!;
    public string IntegrationId { get; private set; } = null!;
    public string DeliveryId { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!;
    public string ProviderCode { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string DelegatedMemberId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public InboxStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public string? ResultLeadId { get; private set; }
    public string? LastResultCode { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    internal void Resume(string correlationId, DateTimeOffset now)
    {
        CorrelationId = correlationId;
        Status = InboxStatus.Received;
        AttemptCount++;
        LastResultCode = null;
        UpdatedAt = now;
    }

    internal void Complete(string leadId, string resultCode, DateTimeOffset now)
    {
        if (Status == InboxStatus.Processed
            && !string.Equals(ResultLeadId, leadId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An Inbox delivery cannot resolve to multiple Leads.");
        }

        Status = InboxStatus.Processed;
        ResultLeadId = leadId;
        LastResultCode = resultCode;
        UpdatedAt = now;
        ProcessedAt = now;
    }

    internal void Fail(string resultCode, DateTimeOffset now)
    {
        if (Status == InboxStatus.Processed)
            return;
        Status = InboxStatus.Failed;
        LastResultCode = resultCode;
        UpdatedAt = now;
    }
}

internal enum InboxStatus
{
    Received,
    Processed,
    Failed
}
