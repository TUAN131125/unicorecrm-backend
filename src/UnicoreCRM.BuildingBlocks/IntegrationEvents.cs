using System.Text.Json;

namespace UnicoreCRM.BuildingBlocks;

public static class IntegrationEventSchema { public const int CurrentVersion = 1; }
public sealed record IntegrationEventDescriptor(string EventType, int SchemaVersion, string Name, string Description, string SourceCategory);
public interface IIntegrationEventCatalog { IReadOnlyList<IntegrationEventDescriptor> Events { get; } bool Admits(string eventType, int schemaVersion); }

public sealed record IntegrationEventEnvelope(
    string EventId,
    string EventType,
    int SchemaVersion,
    string WorkspaceId,
    string SourceOwner,
    string SubjectType,
    string SubjectId,
    long? SubjectVersion,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    JsonElement Data,
    string? CausationId = null);

public sealed record IntegrationEventDraft(string EventId, string CanonicalEnvelopeJson);

public sealed record IntegrationEventLease(
    string SourceOwner,
    string RelayAttemptId,
    DateTimeOffset LeaseExpiresAt,
    IReadOnlyList<IntegrationEventDraft> Events);

public interface IIntegrationEventSource
{
    string SourceOwner { get; }
    IReadOnlyList<IntegrationEventDescriptor> EventDescriptors { get; }
    Task<IntegrationEventLease?> ClaimAsync(int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken);
    Task AcknowledgeAsync(string relayAttemptId, IReadOnlyCollection<string> eventIds, CancellationToken cancellationToken);
    Task ReleaseAsync(string relayAttemptId, IReadOnlyCollection<string> eventIds, string errorCode,
        DateTimeOffset nextEligibleAt, CancellationToken cancellationToken);
}

public static class IntegrationEventSerialization
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string CreateEnvelope(
        string eventId, string eventType, string workspaceId, string sourceOwner,
        string subjectType, string subjectId, long? subjectVersion, DateTimeOffset occurredAt,
        string correlationId, object data, string? causationId = null) =>
        JsonSerializer.Serialize(new IntegrationEventEnvelope(
            eventId, eventType, IntegrationEventSchema.CurrentVersion, workspaceId, sourceOwner,
            subjectType, subjectId, subjectVersion, occurredAt, correlationId,
            JsonSerializer.SerializeToElement(data, Options), causationId), Options);
}
