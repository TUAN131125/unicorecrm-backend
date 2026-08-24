namespace UnicoreCRM.Operations.Tasks.Domain;

internal sealed class TaskOutboxMessage
{
    private TaskOutboxMessage() { }

    internal TaskOutboxMessage(
        string eventType,
        string aggregateId,
        string workspaceId,
        string correlationId,
        string payloadJson,
        DateTimeOffset occurredAt)
    {
        EventId = TaskIds.New("event");
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
