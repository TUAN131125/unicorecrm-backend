namespace UnicoreCRM.Sales.Orders.Contracts;

/// <summary>Bounded internal proof for closing this Deal, not a general Order read API.</summary>
public sealed record ConfirmedDealCommitment(string OrderId, DateTimeOffset ConfirmedAt);
public interface IDealOrderCommitmentVerifier
{
    Task<IDealOrderCommitmentLease> AcquireAsync(string workspaceId, string orderId, string dealId,
        string buyerType, string buyerId, CancellationToken cancellationToken);
}
public interface IDealOrderCommitmentLease : IAsyncDisposable
{
    ConfirmedDealCommitment? Commitment { get; }
}
