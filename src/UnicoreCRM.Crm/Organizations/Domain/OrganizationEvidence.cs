namespace UnicoreCRM.Crm.Organizations.Domain;

internal sealed class OrganizationIdempotencyRecord
{
    private OrganizationIdempotencyRecord() { }
    internal OrganizationIdempotencyRecord(string scopeKey, string workspaceId, string operation, string actorId,
        string targetId, string key, string fingerprint, string responseJson, DateTimeOffset createdAt) =>
        (ScopeKey, WorkspaceId, Operation, ActorId, TargetId, IdempotencyKey, Fingerprint, ResponseJson, CreatedAt) =
        (scopeKey, workspaceId, operation, actorId, targetId, key, fingerprint, responseJson, createdAt);
    internal string ScopeKey { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string Operation { get; private set; } = null!;
    internal string ActorId { get; private set; } = null!;
    internal string TargetId { get; private set; } = null!;
    internal string IdempotencyKey { get; private set; } = null!;
    internal string Fingerprint { get; private set; } = null!;
    internal string ResponseJson { get; private set; } = null!;
    internal DateTimeOffset CreatedAt { get; private set; }
}

internal sealed class OrganizationAuditRecord
{
    private OrganizationAuditRecord() { }
    internal OrganizationAuditRecord(string operation, string workspaceId, string actorId, string aggregateId,
        string requestId, string correlationId, long version, DateTimeOffset occurredAt)
    {
        AuditId = $"organization_audit_{Guid.NewGuid():N}"; Operation = operation; WorkspaceId = workspaceId;
        ActorId = actorId; AggregateId = aggregateId; RequestId = requestId; CorrelationId = correlationId;
        NewVersion = version; OccurredAt = occurredAt;
    }
    internal string AuditId { get; private set; } = null!;
    internal string Operation { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string ActorId { get; private set; } = null!;
    internal string AggregateId { get; private set; } = null!;
    internal string RequestId { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
    internal long NewVersion { get; private set; }
    internal DateTimeOffset OccurredAt { get; private set; }
}

internal sealed class OrganizationOutboxMessage
{
    private OrganizationOutboxMessage() { }
    internal OrganizationOutboxMessage(string type, string aggregateId, string workspaceId, string correlationId,
        string payloadJson, DateTimeOffset occurredAt)
    {
        EventId = $"organization_event_{Guid.NewGuid():N}"; EventType = type; AggregateId = aggregateId;
        WorkspaceId = workspaceId; CorrelationId = correlationId; PayloadJson = payloadJson; OccurredAt = occurredAt;
    }
    internal string EventId { get; private set; } = null!;
    internal string EventType { get; private set; } = null!;
    internal string AggregateId { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
    internal string PayloadJson { get; private set; } = null!;
    internal DateTimeOffset OccurredAt { get; private set; }
}
