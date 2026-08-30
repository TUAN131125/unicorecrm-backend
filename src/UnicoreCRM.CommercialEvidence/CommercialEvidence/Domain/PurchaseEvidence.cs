namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Domain;

internal sealed class PurchaseEvidence
{
    private PurchaseEvidence()
    {
    }

    internal PurchaseEvidence(
        string workspaceId,
        string evidenceId,
        string evidenceType,
        string buyerRefType,
        string buyerRefId,
        string sourceType,
        string? sourceSystem,
        string sourceId,
        DateTimeOffset occurredAt,
        string policyVersion,
        string correlationId)
    {
        WorkspaceId = workspaceId;
        EvidenceId = evidenceId;
        EvidenceType = evidenceType;
        BuyerRefType = buyerRefType;
        BuyerRefId = buyerRefId;
        SourceType = sourceType;
        SourceSystem = sourceSystem;
        SourceId = sourceId;
        OccurredAt = occurredAt;
        PolicyVersion = policyVersion;
        CorrelationId = correlationId;
    }

    internal string WorkspaceId { get; private set; } = null!;
    internal string EvidenceId { get; private set; } = null!;
    internal string EvidenceType { get; private set; } = null!;
    internal string BuyerRefType { get; private set; } = null!;
    internal string BuyerRefId { get; private set; } = null!;
    internal string SourceType { get; private set; } = null!;
    internal string? SourceSystem { get; private set; }
    internal string SourceId { get; private set; } = null!;
    internal DateTimeOffset OccurredAt { get; private set; }
    internal string PolicyVersion { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
}
