using UnicoreCRM.CommercialEvidence.CommercialEvidence.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Application;

internal sealed class CustomerPurchaseHealthSignalReader(ICommercialEvidencePersistence persistence)
    : ICustomerPurchaseHealthSignalReader, ISystemCustomerPurchaseHealthSignalReader
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
        return await ReadCoreAsync(trustedWorkspace.WorkspaceId, buyerRefs, asOf, cancellationToken);
    }

    async Task<IReadOnlyList<CustomerPurchaseHealthSignalSnapshot>> ISystemCustomerPurchaseHealthSignalReader.ReadBatchAsync(
        string workspaceId, IReadOnlyCollection<CustomerPurchaseHealthBuyerRef> buyerRefs, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) || workspaceId.Length > 128) throw new ArgumentException("Workspace identity is invalid.", nameof(workspaceId));
        return await ReadCoreAsync(workspaceId, buyerRefs, asOf, cancellationToken);
    }

    private async Task<IReadOnlyList<CustomerPurchaseHealthSignalSnapshot>> ReadCoreAsync(
        string workspaceId, IReadOnlyCollection<CustomerPurchaseHealthBuyerRef> buyerRefs, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        if (buyerRefs.Count > 250) throw new ArgumentOutOfRangeException(nameof(buyerRefs));
        var normalized = buyerRefs.Distinct().ToArray();
        foreach (var buyerRef in normalized)
        {
            CommercialEvidenceValidation.ValidateBuyerRef(buyerRef.Type, buyerRef.Id);
        }
        if (normalized.Length == 0) return [];

        var rows = await persistence.ReadPurchaseHealthSignalsAsync(
            workspaceId,
            normalized,
            asOf.ToUniversalTime(),
            cancellationToken);
        return rows.GroupBy(row => new CustomerPurchaseHealthBuyerRef(
                CommercialEvidenceValidation.ContractBuyerRefType(row.BuyerRefType), row.BuyerRefId))
            .Select(group =>
            {
                var timestamps = group.Select(row => row.OccurredAt).OrderByDescending(value => value).ToArray();
                var facts = group.First();
                return new CustomerPurchaseHealthSignalSnapshot(group.Key, checked((int)facts.PurchaseCount),
                    facts.FirstPurchaseAt, facts.LastPurchaseAt, timestamps);
            })
            .ToArray();
    }
}
