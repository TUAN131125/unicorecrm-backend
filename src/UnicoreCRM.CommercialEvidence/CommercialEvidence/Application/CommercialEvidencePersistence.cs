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
    Task<IReadOnlyList<PurchaseHealthSignalRow>> ReadPurchaseHealthSignalsAsync(
        string workspaceId,
        IReadOnlyCollection<Contracts.CustomerPurchaseHealthBuyerRef> buyerRefs,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
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

internal sealed class PurchaseHealthSignalRow
{
    public string BuyerRefType { get; init; } = null!;
    public string BuyerRefId { get; init; } = null!;
    public long PurchaseCount { get; init; }
    public DateTimeOffset FirstPurchaseAt { get; init; }
    public DateTimeOffset LastPurchaseAt { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
}
