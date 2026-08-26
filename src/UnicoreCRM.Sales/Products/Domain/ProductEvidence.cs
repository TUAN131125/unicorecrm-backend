namespace UnicoreCRM.Sales.Products.Domain;

internal sealed class ProductIdempotencyRecord
{
    private ProductIdempotencyRecord() { }

    internal ProductIdempotencyRecord(
        string scopeKey,
        string workspaceId,
        string operation,
        string actorId,
        string targetId,
        string idempotencyKey,
        string fingerprint,
        string responseJson,
        DateTimeOffset createdAt)
    {
        ScopeKey = scopeKey;
        WorkspaceId = workspaceId;
        Operation = operation;
        ActorId = actorId;
        TargetId = targetId;
        IdempotencyKey = idempotencyKey;
        Fingerprint = fingerprint;
        ResponseJson = responseJson;
        CreatedAt = createdAt;
    }

    public string ScopeKey { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string Operation { get; private set; } = null!;
    public string ActorId { get; private set; } = null!;
    public string TargetId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string Fingerprint { get; private set; } = null!;
    public string ResponseJson { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}

internal sealed class ProductAuditRecord
{
    private ProductAuditRecord() { }

    internal ProductAuditRecord(
        string operation,
        string workspaceId,
        string actorId,
        string? aggregateId,
        string requestId,
        string correlationId,
        string outcome,
        long? priorVersion,
        long? newVersion,
        DateTimeOffset occurredAt)
    {
        AuditId = ProductIds.New("audit");
        Operation = operation;
        WorkspaceId = workspaceId;
        ActorId = actorId;
        AggregateId = aggregateId;
        RequestId = requestId;
        CorrelationId = correlationId;
        Outcome = outcome;
        PriorVersion = priorVersion;
        NewVersion = newVersion;
        OccurredAt = occurredAt;
    }

    public string AuditId { get; private set; } = null!;
    public string Operation { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string ActorId { get; private set; } = null!;
    public string? AggregateId { get; private set; }
    public string RequestId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public long? PriorVersion { get; private set; }
    public long? NewVersion { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

internal sealed class ProductOutboxMessage
{
    private ProductOutboxMessage() { }

    internal ProductOutboxMessage(
        string eventType,
        string aggregateId,
        string workspaceId,
        string correlationId,
        string payloadJson,
        DateTimeOffset occurredAt)
    {
        EventId = ProductIds.New("event");
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
