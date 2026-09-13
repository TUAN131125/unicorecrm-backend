using System.Text.Json;

namespace UnicoreCRM.BuildingBlocks;

public static class IntegrationEventCatalog
{
    public const int CurrentSchemaVersion = 1;
    public const string ContactChanged = "crm.contact.changed";
    public const string OrganizationChanged = "crm.organization.changed";
    public const string CustomerChanged = "crm.customer.changed";
    public const string RelationshipChanged = "crm.relationship.changed";
    public const string LeadCustomerConverted = "crm.lead.customer_converted";
    public const string ActivityLogged = "crm.activity.logged";

    public static readonly IReadOnlySet<string> EventTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        ContactChanged, OrganizationChanged, CustomerChanged, RelationshipChanged,
        LeadCustomerConverted, ActivityLogged
    };
}

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
            eventId, eventType, IntegrationEventCatalog.CurrentSchemaVersion, workspaceId, sourceOwner,
            subjectType, subjectId, subjectVersion, occurredAt, correlationId,
            JsonSerializer.SerializeToElement(data, Options), causationId), Options);
}
