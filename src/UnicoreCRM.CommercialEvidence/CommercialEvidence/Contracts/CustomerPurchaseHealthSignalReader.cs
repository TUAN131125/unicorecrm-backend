using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Contracts;

public sealed record CustomerPurchaseHealthBuyerRef(PurchaseEvidenceBuyerRefType Type, string Id);

public sealed record CustomerPurchaseHealthSignalSnapshot(
    CustomerPurchaseHealthBuyerRef BuyerRef,
    int PurchaseCount,
    DateTimeOffset? FirstPurchaseAt,
    DateTimeOffset? LastPurchaseAt,
    IReadOnlyList<DateTimeOffset> RecentPurchaseTimestamps);

public interface ICustomerPurchaseHealthSignalReader
{
    Task<CustomerPurchaseHealthSignalSnapshot?> ReadAsync(
        TrustedWorkspaceContext trustedWorkspace,
        CustomerPurchaseHealthBuyerRef buyerRef,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CustomerPurchaseHealthSignalSnapshot>> ReadBatchAsync(
        TrustedWorkspaceContext trustedWorkspace,
        IReadOnlyCollection<CustomerPurchaseHealthBuyerRef> buyerRefs,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}

/// <summary>Owner-to-owner system read used only by Customers for bounded background Health evaluation.</summary>
public interface ISystemCustomerPurchaseHealthSignalReader
{
    Task<IReadOnlyList<CustomerPurchaseHealthSignalSnapshot>> ReadBatchAsync(
        string workspaceId,
        IReadOnlyCollection<CustomerPurchaseHealthBuyerRef> buyerRefs,
        DateTimeOffset asOf,
        CancellationToken cancellationToken);
}
