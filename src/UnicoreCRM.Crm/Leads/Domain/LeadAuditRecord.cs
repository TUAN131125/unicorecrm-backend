namespace UnicoreCRM.Crm.Leads.Domain;

internal sealed class LeadAuditRecord
{
    private LeadAuditRecord() { }

    internal LeadAuditRecord(
        string operation,
        string workspaceId,
        string actorId,
        string? aggregateId,
        string requestId,
        string correlationId,
        string outcome,
        long? priorVersion,
        long? newVersion,
        DateTimeOffset occurredAt,
        string actorType = "Member",
        string? delegatedSubjectId = null,
        string? sourceReference = null)
    {
        AuditId = LeadIds.New("audit");
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
        ActorType = actorType;
        DelegatedSubjectId = delegatedSubjectId;
        SourceReference = sourceReference;
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
    public string ActorType { get; private set; } = null!;
    public string? DelegatedSubjectId { get; private set; }
    public string? SourceReference { get; private set; }
}
