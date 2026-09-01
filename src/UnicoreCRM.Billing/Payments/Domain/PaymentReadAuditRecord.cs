namespace UnicoreCRM.Billing.Payments.Domain;

/// <summary>
/// Payments-owned proof that data was disclosed through an admitted Payments read.
///
/// <para>Per the frozen <c>READ_ACCESS_LOG</c> semantics, this is a different artifact from the
/// AccessControl-owned <c>AuthorizationDecisions</c> (a capability was evaluated) and
/// <c>RecordAccessDecisions</c> (a record policy decision was taken). None substitutes for another,
/// and only a successful disclosure appends one of these.</para>
///
/// <para>It carries identifiers, an operation name, an outcome and a timestamp - never a business
/// value, never a foreign-owner attribute and never a result cardinality. Every value originates in
/// the trusted Workspace context or the request metadata, never in caller-supplied authority data.
/// </para>
/// </summary>
internal sealed class PaymentReadAuditRecord
{
    private PaymentReadAuditRecord() { }

    internal PaymentReadAuditRecord(
        string operation,
        string workspaceId,
        string actorId,
        string? recordId,
        string requestId,
        string correlationId,
        long? resourceVersion,
        DateTimeOffset occurredAt)
    {
        AuditId = $"payment_read_audit_{Guid.NewGuid():N}";
        Operation = operation;
        WorkspaceId = workspaceId;
        ActorId = actorId;
        RecordId = recordId;
        RequestId = requestId;
        CorrelationId = correlationId;
        Outcome = ReadOutcome;
        ResourceVersion = resourceVersion;
        OccurredAt = occurredAt;
    }

    /// <summary>The frozen discriminator identifying this row as successful read evidence.</summary>
    internal const string ReadOutcome = "READ";

    internal string AuditId { get; private set; } = null!;

    /// <summary>The exact admitted operationId, never a route or a handler name.</summary>
    internal string Operation { get; private set; } = null!;

    internal string WorkspaceId { get; private set; } = null!;

    internal string ActorId { get; private set; } = null!;

    /// <summary>The canonical Payments record for a RESOURCE read; null for a WORKSPACE/list read.</summary>
    internal string? RecordId { get; private set; }

    internal string RequestId { get; private set; } = null!;

    internal string CorrelationId { get; private set; } = null!;

    internal string Outcome { get; private set; } = null!;

    /// <summary>The disclosed record's version where the Payments resource maintains one.</summary>
    internal long? ResourceVersion { get; private set; }

    internal DateTimeOffset OccurredAt { get; private set; }
}
