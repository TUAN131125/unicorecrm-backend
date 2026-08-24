namespace UnicoreCRM.Crm.Deals.Domain;

internal sealed class DealAuditRecord
{
    private DealAuditRecord() { }

    internal DealAuditRecord(
        string operation,
        string workspaceId,
        string actorId,
        string? aggregateId,
        string requestId,
        string correlationId,
        string outcome,
        long? priorVersion,
        long? newVersion,
        DateTimeOffset occurredAt)
    {
        AuditId = DealIds.New("audit");
        Operation = operation;
        WorkspaceId = workspaceId;
        ActorId = actorId;
        AggregateId = aggregateId;
        RequestId = requestId;
        CorrelationId = correlationId;
        Outcome = outcome;
        PriorVersion = priorVersion;
        NewVersion = newVersion;
        OccurredAt = occurredAt;
    }

    public string AuditId { get; private set; } = null!;
    public string Operation { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string ActorId { get; private set; } = null!;
    public string? AggregateId { get; private set; }
    public string RequestId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public long? PriorVersion { get; private set; }
    public long? NewVersion { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
