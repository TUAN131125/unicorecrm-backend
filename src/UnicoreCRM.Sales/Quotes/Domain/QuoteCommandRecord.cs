namespace UnicoreCRM.Sales.Quotes.Domain;

internal sealed class QuoteCommandRecord
{
    private QuoteCommandRecord() { }
    internal QuoteCommandRecord(string scopeKey, string workspaceId, string actorId, string operation,
        string quoteId, string fingerprint, string responseJson, string auditId, string requestId,
        string correlationId, long? priorVersion, long version, DateTimeOffset now)
    {
        ScopeKey = scopeKey; WorkspaceId = workspaceId; ActorId = actorId; Operation = operation;
        QuoteId = quoteId; Fingerprint = fingerprint; ResponseJson = responseJson; AuditId = auditId;
        RequestId = requestId; CorrelationId = correlationId; PriorVersion = priorVersion;
        ResourceVersion = version; OccurredAt = now;
    }
    public string ScopeKey { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string ActorId { get; private set; } = null!;
    public string Operation { get; private set; } = null!;
    public string QuoteId { get; private set; } = null!;
    public string Fingerprint { get; private set; } = null!;
    public string ResponseJson { get; private set; } = null!;
    public string AuditId { get; private set; } = null!;
    public string RequestId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public long? PriorVersion { get; private set; }
    public long ResourceVersion { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

internal sealed class QuoteOutboxMessage
{
    private QuoteOutboxMessage() { }
    internal QuoteOutboxMessage(string eventId, string workspaceId, string quoteId, string type, string correlationId, string payload, DateTimeOffset now)
    { EventId = eventId; WorkspaceId = workspaceId; QuoteId = quoteId; EventType = type; CorrelationId = correlationId; PayloadJson = payload; OccurredAt = now; }
    public string EventId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string QuoteId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
}
