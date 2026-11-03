using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Sales.Orders.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;

namespace UnicoreCRM.Sales.Orders.Infrastructure.Persistence;

internal sealed class DealOrderCommitmentVerifier(OrdersDbContext db, IDealQuoteCommitmentBinding quotes) : IDealOrderCommitmentVerifier
{
    public async Task<IDealOrderCommitmentLease> AcquireAsync(string workspaceId, string orderId, string dealId,
        string buyerType, string buyerId, CancellationToken ct)
    {
        // This read-only lease holds source row locks until the caller's Deal commit completes.
        // Only the Deal transaction writes; no distributed write transaction is claimed.
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.WorkspaceId == workspaceId && o.OrderId == orderId, ct);
            if (order is null || order.State != "CONFIRMED" || order.ArchivedAt is not null || order.CancelledAt is not null
                || order.ConfirmedAt is null || order.SourceDealId != dealId || order.BuyerType != buyerType || order.BuyerId != buyerId
                || order.SourceQuoteId is null || order.SourceAcceptanceId is null || order.SourceQuoteResourceVersion is null
                || order.CommercialSnapshotFingerprint is null || order.AcceptanceJson is null)
                return new Lease(null, transaction);
            var bound = await quotes.VerifyAsync(new OwnerTransaction(db.Database.GetDbConnection(), transaction.GetDbTransaction()),
                new DealCommitmentBinding(workspaceId, order.SourceQuoteId, order.SourceAcceptanceId, orderId,
                    dealId, buyerType, buyerId, order.CommercialSnapshotFingerprint, order.SourceQuoteResourceVersion.Value, order.AcceptanceJson), ct);
            return new Lease(bound ? new(orderId, order.ConfirmedAt.Value) : null, transaction);
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
    private sealed class Lease(ConfirmedDealCommitment? commitment, IDbContextTransaction transaction) : IDealOrderCommitmentLease
    {
        public ConfirmedDealCommitment? Commitment { get; } = commitment;
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
