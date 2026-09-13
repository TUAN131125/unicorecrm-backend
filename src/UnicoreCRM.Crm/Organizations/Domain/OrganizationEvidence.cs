using System.Text.Json;
using UnicoreCRM.BuildingBlocks;

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
        if (type is "ORGANIZATION_CREATED" or "ORGANIZATION_UPDATED" or "ORGANIZATION_ARCHIVED")
        {
            using var payload = JsonDocument.Parse(payloadJson);
            var data = payload.RootElement.Clone();
            IntegrationEnvelopeJson = IntegrationEventSerialization.CreateEnvelope(EventId,
                OrganizationIntegrationEvents.Changed, workspaceId, "Organizations", "ORGANIZATION",
                aggregateId, data.GetProperty("resourceVersion").GetInt64(), occurredAt, correlationId, data);
            ExportState = "PENDING";
        }
    }
    internal string EventId { get; private set; } = null!;
    internal string EventType { get; private set; } = null!;
    internal string AggregateId { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
    internal string PayloadJson { get; private set; } = null!;
    internal DateTimeOffset OccurredAt { get; private set; }
    internal string? IntegrationEnvelopeJson { get; private set; }
    internal string? ExportState { get; private set; }
    internal int ExportAttemptCount { get; private set; }
    internal string? RelayAttemptId { get; private set; }
    internal DateTimeOffset? LeaseExpiresAt { get; private set; }
    internal DateTimeOffset? NextEligibleAt { get; private set; }
    internal DateTimeOffset? PublishedAt { get; private set; }
    internal string? LastRelayError { get; private set; }
    internal void Lease(string id, DateTimeOffset until) { ExportState="LEASED"; RelayAttemptId=id; LeaseExpiresAt=until; ExportAttemptCount++; }
    internal void Publish(string id, DateTimeOffset now) { if(RelayAttemptId!=id||ExportState!="LEASED")throw new InvalidOperationException("Stale relay attempt.");ExportState="PUBLISHED";PublishedAt=now;RelayAttemptId=null;LeaseExpiresAt=null;LastRelayError=null; }
    internal void Release(string id,string error,DateTimeOffset next) { if(RelayAttemptId!=id||ExportState!="LEASED")return;ExportState="PENDING";RelayAttemptId=null;LeaseExpiresAt=null;NextEligibleAt=next;LastRelayError=error[..Math.Min(512,error.Length)]; }
}
