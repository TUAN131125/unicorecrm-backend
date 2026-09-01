namespace UnicoreCRM.Sales.Orders.Domain;

/// <summary>
/// Orders-owned proof that an admitted Order read successfully disclosed data.
/// This is separate from AccessControl authorization and record-decision evidence.
/// </summary>
internal sealed class OrderReadAuditRecord
{
    private OrderReadAuditRecord() { }

    internal OrderReadAuditRecord(
        string operation,
        string workspaceId,
        string actorId,
        string? recordId,
        string requestId,
        string correlationId,
        long? resourceVersion,
        DateTimeOffset occurredAt)
    {
        AuditId = $"order_read_audit_{Guid.NewGuid():N}";
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

    internal const string ReadOutcome = "READ";

    internal string AuditId { get; private set; } = null!;
    internal string Operation { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string ActorId { get; private set; } = null!;
    internal string? RecordId { get; private set; }
    internal string RequestId { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
    internal string Outcome { get; private set; } = null!;
    internal long? ResourceVersion { get; private set; }
    internal DateTimeOffset OccurredAt { get; private set; }
}
