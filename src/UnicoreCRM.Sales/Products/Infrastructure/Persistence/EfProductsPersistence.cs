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

    public Task<Product?> LoadProductAsync(string productId, CancellationToken cancellationToken) =>
        dbContext.Products.SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);

    public Task<Product?> ReadProductAsync(string productId, CancellationToken cancellationToken) =>
        dbContext.Products.AsNoTracking().SingleOrDefaultAsync(item => item.ProductId == productId, cancellationToken);

    public async Task<IReadOnlyList<Product>> ReadProductsAsync(string workspaceId, CancellationToken cancellationToken) =>
        await dbContext.Products
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.ProductId)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Product>> LoadProductsAsync(
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken) =>
        await dbContext.Products.Where(item => productIds.Contains(item.ProductId)).ToArrayAsync(cancellationToken);

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
