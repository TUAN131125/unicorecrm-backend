using System.Text.Json;
using UnicoreCRM.BuildingBlocks;

namespace UnicoreCRM.Crm.Contacts.Domain;

/// <summary>
/// Contacts-owned immutable command evidence. It is written only when this owner actually committed
/// a mutation, in the same transaction as that mutation. It is distinct from
/// <see cref="ContactReadAuditRecord"/>, which proves successful read disclosure, and from the
/// AccessControl authorization and record-decision records; none of the three substitutes for
/// another.
/// </summary>
internal sealed class ContactAuditRecord
{
    private ContactAuditRecord() { }

    internal ContactAuditRecord(
        string operation,
        string workspaceId,
        string actorId,
        string aggregateId,
        string requestId,
        string correlationId,
        string outcome,
        long? newVersion,
        DateTimeOffset occurredAt,
        string actorType = "Member")
    {
        AuditId = ContactIds.New("contact_audit");
        Operation = operation;
        WorkspaceId = workspaceId;
        ActorId = actorId;
        AggregateId = aggregateId;
        RequestId = requestId;
        CorrelationId = correlationId;
        Outcome = outcome;
        NewVersion = newVersion;
        OccurredAt = occurredAt;
        ActorType = actorType;
    }

    internal string AuditId { get; private set; } = null!;
    internal string Operation { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string ActorId { get; private set; } = null!;
    internal string AggregateId { get; private set; } = null!;
    internal string RequestId { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
    internal string Outcome { get; private set; } = null!;
    internal long? NewVersion { get; private set; }
    internal DateTimeOffset OccurredAt { get; private set; }
    internal string ActorType { get; private set; } = null!;
}

/// <summary>
/// Contacts-owned outbox staging. Contacts stages only its own leg; it never writes into another
/// owner's outbox and never emits another owner's event.
/// </summary>
internal sealed class ContactOutboxMessage
{
    private ContactOutboxMessage() { }

    internal ContactOutboxMessage(
        string eventType,
        string aggregateId,
        string workspaceId,
        string correlationId,
        string payloadJson,
        DateTimeOffset occurredAt)
    {
        EventId = ContactIds.New("contact_event");
        EventType = eventType;
        AggregateId = aggregateId;
        WorkspaceId = workspaceId;
        CorrelationId = correlationId;
        PayloadJson = payloadJson;
        OccurredAt = occurredAt;
        Admit(eventType, payloadJson);
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

    internal void Lease(string attemptId, DateTimeOffset expiresAt)
    { ExportState = "LEASED"; RelayAttemptId = attemptId; LeaseExpiresAt = expiresAt; ExportAttemptCount++; }
    internal void Publish(string attemptId, DateTimeOffset now)
    { if (RelayAttemptId != attemptId || ExportState != "LEASED") throw new InvalidOperationException("Stale relay attempt."); ExportState = "PUBLISHED"; PublishedAt = now; RelayAttemptId = null; LeaseExpiresAt = null; LastRelayError = null; }
    internal void Release(string attemptId, string error, DateTimeOffset next)
    { if (RelayAttemptId != attemptId || ExportState != "LEASED") return; ExportState = "PENDING"; RelayAttemptId = null; LeaseExpiresAt = null; NextEligibleAt = next; LastRelayError = error[..Math.Min(512, error.Length)]; }

    private void Admit(string type, string payloadJson)
    {
        using var payload = JsonDocument.Parse(payloadJson);
        var data = payload.RootElement;
        string? publicType = type switch
        {
            "CONTACT_CREATED" or "CONTACT_UPDATED" or "CONTACT_ARCHIVED" => ContactIntegrationEvents.ContactChanged,
            "CONTACT_ORGANIZATION_RELATIONSHIP_CREATED" or "CONTACT_ORGANIZATION_RELATIONSHIP_UPDATED" or "CONTACT_ORGANIZATION_RELATIONSHIP_ENDED"
                or "CONTACT_CUSTOMER_RELATIONSHIP_CREATED" or "CONTACT_CUSTOMER_RELATIONSHIP_UPDATED" or "CONTACT_CUSTOMER_RELATIONSHIP_ENDED" => ContactIntegrationEvents.RelationshipChanged,
            _ => null
        };
        if (publicType is null) return;
        IntegrationEnvelopeJson = IntegrationEventSerialization.CreateEnvelope(EventId, publicType, WorkspaceId,
            "Contacts", publicType == ContactIntegrationEvents.RelationshipChanged ? "RELATIONSHIP" : "CONTACT",
            data.TryGetProperty("relationshipId", out var relationship) ? relationship.GetString() ?? AggregateId : AggregateId,
            data.TryGetProperty("resourceVersion", out var version) ? version.GetInt64() : data.TryGetProperty("contactVersion", out var contactVersion) ? contactVersion.GetInt64() : null,
            OccurredAt, CorrelationId, data);
        ExportState = "PENDING";
    }
}

/// <summary>
/// Contacts-owned receipt for a coordinator-supplied conversion key. It retains the resolved
/// identity, original version/name and creation decision so a lost acknowledgment can be replayed
/// without re-reading mutable result facts, whether or not the coordinator recorded the result.
///
/// It is not the workflow idempotency record. Payload-fingerprint comparison and
/// <c>IDEMPOTENCY_KEY_REUSED</c> belong to the coordinator's own idempotency boundary; this owner
/// records only its own resolution result.
/// </summary>
internal sealed class ContactConversionRecord
{
    private ContactConversionRecord() { }

    internal ContactConversionRecord(
        string scopeKey,
        string workspaceId,
        string conversionKey,
        string contactId,
        string resultJson,
        DateTimeOffset createdAt)
    {
        ScopeKey = scopeKey;
        WorkspaceId = workspaceId;
        ConversionKey = conversionKey;
        ContactId = contactId;
        ResultJson = resultJson;
        CreatedAt = createdAt;
    }

    internal string ScopeKey { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string ConversionKey { get; private set; } = null!;
    internal string ContactId { get; private set; } = null!;
    // Immutable resolution facts, stored in the owner's transaction before acknowledgment.
    internal string? ResultJson { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
}
