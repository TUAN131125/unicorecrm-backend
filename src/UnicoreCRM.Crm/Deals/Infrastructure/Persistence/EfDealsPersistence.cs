using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Infrastructure.Persistence;

internal sealed class EfDealsPersistence(DealsDbContext dbContext) : IDealsPersistence
{
    public async Task<IDealsTransaction> BeginSerializableAsync(CancellationToken cancellationToken) =>
        new DealsTransaction(await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    public Task<Deal?> LoadDealAsync(string workspaceId, string dealId, CancellationToken cancellationToken) =>
        dbContext.Deals.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.DealId == dealId, cancellationToken);

    public Task<Deal?> ReadDealAsync(string workspaceId, string dealId, CancellationToken cancellationToken) =>
        dbContext.Deals.AsNoTracking().SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.DealId == dealId, cancellationToken);

    public async Task<IReadOnlyList<Deal>> ReadDealsAsync(string workspaceId, CancellationToken cancellationToken) =>
        await dbContext.Deals.AsNoTracking().Where(item => item.WorkspaceId == workspaceId).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Deal>> LoadDealsAsync(
        string workspaceId,
        IReadOnlyCollection<string> dealIds,
        CancellationToken cancellationToken) =>
        await dbContext.Deals
            .Where(item => item.WorkspaceId == workspaceId && dealIds.Contains(item.DealId))
            .ToArrayAsync(cancellationToken);

    public Task<DealIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken) =>
        dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);

    public void AddDeal(Deal deal) => dbContext.Deals.Add(deal);
    public void AddIdempotency(DealIdempotencyRecord record) => dbContext.IdempotencyRecords.Add(record);
    public void AddAudit(DealAuditRecord audit) => dbContext.AuditRecords.Add(audit);
    public void AddOutbox(DealOutboxMessage message) => dbContext.OutboxMessages.Add(message);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new DealsPersistenceConcurrencyException(exception);
        }
    }

    private sealed class DealsTransaction(IDbContextTransaction transaction) : IDealsTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
