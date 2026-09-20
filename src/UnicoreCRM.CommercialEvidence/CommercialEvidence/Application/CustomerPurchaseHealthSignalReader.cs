using UnicoreCRM.CommercialEvidence.CommercialEvidence.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Application;

internal sealed class CustomerPurchaseHealthSignalReader(ICommercialEvidencePersistence persistence)
    : ICustomerPurchaseHealthSignalReader
{
    public async Task<CustomerPurchaseHealthSignalSnapshot?> ReadAsync(
        TrustedWorkspaceContext trustedWorkspace,
        CustomerPurchaseHealthBuyerRef buyerRef,
        DateTimeOffset asOf,
        CancellationToken cancellationToken) =>
        (await ReadBatchAsync(trustedWorkspace, [buyerRef], asOf, cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<CustomerPurchaseHealthSignalSnapshot>> ReadBatchAsync(
        TrustedWorkspaceContext trustedWorkspace,
        IReadOnlyCollection<CustomerPurchaseHealthBuyerRef> buyerRefs,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        CommercialEvidenceValidation.ValidateTrustedWorkspace(trustedWorkspace);
        if (buyerRefs.Count > 250) throw new ArgumentOutOfRangeException(nameof(buyerRefs));
        var normalized = buyerRefs.Distinct().ToArray();
        foreach (var buyerRef in normalized)
        {
            CommercialEvidenceValidation.ValidateBuyerRef(buyerRef.Type, buyerRef.Id);
        }
        if (normalized.Length == 0) return [];

        var rows = await persistence.ReadPurchaseHealthSignalsAsync(
            trustedWorkspace.WorkspaceId,
            normalized,
            asOf.ToUniversalTime(),
            cancellationToken);
        return rows.GroupBy(row => new CustomerPurchaseHealthBuyerRef(
                CommercialEvidenceValidation.ContractBuyerRefType(row.BuyerRefType), row.BuyerRefId))
            .Select(group =>
            {
                var timestamps = group.Select(row => row.OccurredAt).OrderByDescending(value => value).ToArray();
                return new CustomerPurchaseHealthSignalSnapshot(group.Key, timestamps.Length,
                    timestamps[^1], timestamps[0], timestamps.Take(6).ToArray());
            })
            .ToArray();
    }
}
