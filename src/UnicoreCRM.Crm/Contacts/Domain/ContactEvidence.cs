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
    }

    internal string EventId { get; private set; } = null!;
    internal string EventType { get; private set; } = null!;
    internal string AggregateId { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
    internal string PayloadJson { get; private set; } = null!;
    internal DateTimeOffset OccurredAt { get; private set; }
}

/// <summary>
/// Contacts-owned mapping from a coordinator-supplied conversion key to the Contact this owner
/// created for it. It makes the participant's own replay deterministic: a re-drive under the same
/// conversion key returns the same <c>ContactId</c> instead of creating a second Contact, whether or
/// not the coordinator's durable anchor survived.
///
/// It is not the workflow idempotency record. Payload-fingerprint comparison and
/// <c>IDEMPOTENCY_KEY_REUSED</c> belong to the coordinator's own idempotency boundary; this owner
/// records only which Contact its own conversion produced.
/// </summary>
internal sealed class ContactConversionRecord
{
    private ContactConversionRecord() { }

    internal ContactConversionRecord(
        string scopeKey,
        string workspaceId,
        string conversionKey,
        string contactId,
        DateTimeOffset createdAt)
    {
        ScopeKey = scopeKey;
        WorkspaceId = workspaceId;
        ConversionKey = conversionKey;
        ContactId = contactId;
        CreatedAt = createdAt;
    }

    internal string ScopeKey { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string ConversionKey { get; private set; } = null!;
    internal string ContactId { get; private set; } = null!;
    internal DateTimeOffset CreatedAt { get; private set; }
}
