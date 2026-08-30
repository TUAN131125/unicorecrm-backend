namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Domain;

internal sealed class CommercialEvidenceAuditRecord
{
    private CommercialEvidenceAuditRecord()
    {
    }

    internal CommercialEvidenceAuditRecord(
        string auditId,
        string workspaceId,
        string evidenceId,
        string operation,
        string correlationId,
        DateTimeOffset occurredAt,
        string policyVersion)
    {
        AuditId = auditId;
        WorkspaceId = workspaceId;
        EvidenceId = evidenceId;
        Operation = operation;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
        PolicyVersion = policyVersion;
    }

    internal string AuditId { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string EvidenceId { get; private set; } = null!;
    internal string Operation { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
    internal DateTimeOffset OccurredAt { get; private set; }
    internal string PolicyVersion { get; private set; } = null!;
}
