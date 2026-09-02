using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Infrastructure.Persistence;

internal sealed class EfProductsPersistence(ProductsDbContext dbContext) : IProductsPersistence
{
    public async Task<IProductsTransaction> BeginSerializableAsync(CancellationToken cancellationToken) =>
        new ProductsTransaction(await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    // Both point lookups constrain WorkspaceId in SQL. A foreign-Workspace Product is never
    // materialised, so no caller can inspect it and no caller can turn its existence into a
    // different status code, error body or version.
    public Task<Product?> LoadProductAsync(string workspaceId, string productId, CancellationToken cancellationToken) =>
        dbContext.Products.SingleOrDefaultAsync(
            item => item.WorkspaceId == workspaceId && item.ProductId == productId, cancellationToken);

    public Task<Product?> ReadProductAsync(string workspaceId, string productId, CancellationToken cancellationToken) =>
        dbContext.Products.AsNoTracking().SingleOrDefaultAsync(
            item => item.WorkspaceId == workspaceId && item.ProductId == productId, cancellationToken);

    public async Task<IReadOnlyList<Product>> ReadProductsAsync(
        string workspaceId,
        string? scopeOwnerMemberId,
        CancellationToken cancellationToken)
    {
        // Product carries no member-owner column, so an owner-scoped request can match nothing. The
        // caller resolves that to an empty result before reaching persistence; this guard keeps the
        // contract honest if a future caller forgets.
        if (scopeOwnerMemberId is not null)
            return [];
        return await dbContext.Products
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.ProductId)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> LoadProductsAsync(
        string workspaceId,
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken) =>
        await dbContext.Products
            .Where(item => item.WorkspaceId == workspaceId && productIds.Contains(item.ProductId))
            .ToArrayAsync(cancellationToken);

    public Task<bool> SkuExistsAsync(
        string workspaceId,
        string normalizedSku,
        string? exceptProductId,
        CancellationToken cancellationToken) =>
        dbContext.Products.AnyAsync(
            item => item.WorkspaceId == workspaceId
                && item.NormalizedSku == normalizedSku
                && (exceptProductId == null || item.ProductId != exceptProductId),
            cancellationToken);

    // The anchor and the overrides are two tables, so they are read inside one serialisable
    // transaction. Without it a concurrent configuration commit could be observed by one query and
    // not the other, and the response would pair a document with a revision that never described it.
    public async Task<ProductConfigurationState> ReadProductConfigurationAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var anchor = await dbContext.ProductConfigurationDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
        var overrides = await dbContext.ProductConfigurationTypeOverrides
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // No anchor is the valid sparse state of a Workspace that has never committed a
        // configuration change. It reports revision 0 and is never created by this read.
        return new ProductConfigurationState(anchor?.Revision ?? 0L, overrides);
    }

    public Task<ProductIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken) =>
        dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);

    public void AddProduct(Product product) => dbContext.Products.Add(product);
    public void AddIdempotency(ProductIdempotencyRecord record) => dbContext.IdempotencyRecords.Add(record);
    public void AddAudit(ProductAuditRecord audit) => dbContext.AuditRecords.Add(audit);
    public void AddOutbox(ProductOutboxMessage message) => dbContext.OutboxMessages.Add(message);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ProductsPersistenceConcurrencyException(exception);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraint(exception))
        {
            throw new ProductsPersistenceUniqueException(exception);
        }
    }

    private static bool IsUniqueConstraint(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 })
                return true;
        }
        return false;
    }

    private sealed class ProductsTransaction(IDbContextTransaction transaction) : IProductsTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
