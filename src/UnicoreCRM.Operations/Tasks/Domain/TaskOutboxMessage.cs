using System.Text.Json;
using UnicoreCRM.BuildingBlocks;

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
        if (eventType == "ACTIVITY_LOGGED")
        {
            using var payload = JsonDocument.Parse(payloadJson); var data = payload.RootElement.Clone();
            IntegrationEnvelopeJson = IntegrationEventSerialization.CreateEnvelope(EventId, TaskIntegrationEvents.ActivityLogged,
                workspaceId, "Tasks", "ACTIVITY", aggregateId, data.GetProperty("resourceVersion").GetInt64(), occurredAt, correlationId, data);
            ExportState = "PENDING";
        }
    }

    public string EventId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public string AggregateId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    internal string? IntegrationEnvelopeJson { get; private set; } internal string? ExportState { get; private set; }
    internal int ExportAttemptCount { get; private set; } internal string? RelayAttemptId { get; private set; }
    internal DateTimeOffset? LeaseExpiresAt { get; private set; } internal DateTimeOffset? NextEligibleAt { get; private set; }
    internal DateTimeOffset? PublishedAt { get; private set; } internal string? LastRelayError { get; private set; }
    internal void Lease(string id,DateTimeOffset until){ExportState="LEASED";RelayAttemptId=id;LeaseExpiresAt=until;ExportAttemptCount++;}
    internal void Publish(string id,DateTimeOffset now){if(RelayAttemptId!=id||ExportState!="LEASED")throw new InvalidOperationException("Stale relay attempt.");ExportState="PUBLISHED";PublishedAt=now;RelayAttemptId=null;LeaseExpiresAt=null;LastRelayError=null;}
    internal void Release(string id,string error,DateTimeOffset next){if(RelayAttemptId!=id||ExportState!="LEASED")return;ExportState="PENDING";RelayAttemptId=null;LeaseExpiresAt=null;NextEligibleAt=next;LastRelayError=error[..Math.Min(512,error.Length)];}
}
