namespace UnicoreCRM.Platform.IdentityAuth.Domain;

internal sealed class IdentityIdempotencyRecord
{
    private IdentityIdempotencyRecord() { }

    internal IdentityIdempotencyRecord(string operation, string key, string fingerprint, string resourceId, DateTimeOffset createdAt)
    {
        Operation = operation;
        Key = key;
        Fingerprint = fingerprint;
        ResourceId = resourceId;
        CreatedAt = createdAt;
    }

    public long Id { get; private set; }
    public string Operation { get; private set; } = null!;
    public string Key { get; private set; } = null!;
    public string Fingerprint { get; private set; } = null!;
    public string ResourceId { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}

internal sealed class IdentityAuditRecord
{
    private IdentityAuditRecord() { }

    internal IdentityAuditRecord(string operation, string outcome, string? accountId, string correlationId, DateTimeOffset occurredAt)
    {
        Operation = operation;
        Outcome = outcome;
        AccountId = accountId;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
    }

    public long Id { get; private set; }
    public string Operation { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public string? AccountId { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
}

internal sealed class IdentitySecurityEvent
{
    private IdentitySecurityEvent() { }

    internal IdentitySecurityEvent(string eventType, string? accountId, string correlationId, DateTimeOffset occurredAt)
    {
        EventId = IdentityIds.New("evt");
        EventType = eventType;
        AccountId = accountId;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
    }

    public string EventId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public string? AccountId { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
}
