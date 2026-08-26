namespace UnicoreCRM.Operations.Support.Domain;

/// <summary>
/// Owner-local Support outbox evidence, written inside the same transaction as the Support
/// mutation that produced it. Only the event types the current operation registry declares
/// for Support are emitted; Support publishes nothing speculatively and owns no dispatcher.
/// </summary>
internal sealed class SupportOutboxMessage
{
    private SupportOutboxMessage() { }

    internal SupportOutboxMessage(
        string eventType,
        string aggregateId,
        string workspaceId,
        string correlationId,
        string payloadJson,
        DateTimeOffset occurredAt)
    {
        EventId = SupportIds.New("event");
        EventType = eventType;
        AggregateId = aggregateId;
        WorkspaceId = workspaceId;
        CorrelationId = correlationId;
        PayloadJson = payloadJson;
        OccurredAt = occurredAt;
    }

    public string EventId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public string AggregateId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
}
