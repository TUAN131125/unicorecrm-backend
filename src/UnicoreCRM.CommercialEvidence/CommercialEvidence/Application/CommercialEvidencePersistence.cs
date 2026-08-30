using UnicoreCRM.CommercialEvidence.CommercialEvidence.Domain;

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Application;

internal enum CommercialEvidenceUniqueConflict
{
    SourceIdentity,
    AggregateIdentity
}

internal sealed class CommercialEvidenceUniqueConflictException(
    CommercialEvidenceUniqueConflict conflict,
    Exception innerException) : Exception("A CommercialEvidence uniqueness boundary rejected the append.", innerException)
{
    internal CommercialEvidenceUniqueConflict Conflict { get; } = conflict;
}

internal interface ICommercialEvidencePersistence
{
    Task<PurchaseEvidence?> FindOriginalByOrderSourceAsync(
        string workspaceId,
        string orderId,
        CancellationToken cancellationToken);

    Task<PurchaseEvidence?> ReadOriginalByIdAsync(
        string workspaceId,
        string evidenceId,
        CancellationToken cancellationToken);

    void Add(PurchaseEvidence evidence, CommercialEvidenceAuditRecord audit);
    void ClearTrackedChanges();
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
