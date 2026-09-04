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

    public async Task<IReadOnlyList<Product>> ReadProductSnapshotsAsync(
        string workspaceId,
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken) =>
        await dbContext.Products
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && productIds.Contains(item.ProductId))
            .ToArrayAsync(cancellationToken);

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
        var state = await QueryProductConfigurationAsync(workspaceId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return state;
    }

    // Deliberately opens no transaction: a Product command already holds a serializable one, and
    // starting another here would either nest or read a second, independent snapshot.
    public Task<ProductConfigurationState> LoadProductConfigurationForCommandAsync(
        string workspaceId,
        CancellationToken cancellationToken) =>
        QueryProductConfigurationAsync(workspaceId, cancellationToken);

    // The mutation reads the exact rows it is about to write, under an update lock held for the rest
    // of the caller's serializable transaction. Reading them unlocked and writing afterwards would
    // reintroduce the check-then-write gap the command path already closes, and reading them under a
    // plain shared lock would make two concurrent mutations deadlock on the shared-to-exclusive
    // upgrade rather than serialize.
    public async Task<ProductConfigurationState> LockProductConfigurationForMutationAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        var anchor = await dbContext.ProductConfigurationDocuments
            .FromSqlInterpolated(
                $"SELECT [WorkspaceId], [Revision] FROM [products].[ProductConfigurationDocuments] WITH (UPDLOCK, HOLDLOCK) WHERE [WorkspaceId] = {workspaceId}")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        var overrides = await dbContext.ProductConfigurationTypeOverrides
            .FromSqlInterpolated(
                $"SELECT [WorkspaceId], [ProductTypeCode], [Status] FROM [products].[ProductConfigurationTypeOverrides] WITH (UPDLOCK, HOLDLOCK) WHERE [WorkspaceId] = {workspaceId}")
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        // The trusted mark is evidence rather than state under mutation: it is only ever raised, and
        // by one atomic monotonic statement, so it needs no update lock of its own.
        var trusted = await dbContext.ProductConfigurationTrustedRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
        return new ProductConfigurationState(
            anchor?.Revision ?? 0L,
            trusted?.GreatestTrustedRevision ?? 0L,
            overrides);
    }

    public async Task ApplyProductConfigurationTypeStatusAsync(
        string workspaceId,
        string productTypeCode,
        string? overrideStatus,
        long newRevision,
        CancellationToken cancellationToken)
    {
        // The rows are already update-locked by LockProductConfigurationForMutationAsync, so these
        // reads observe the same snapshot the caller decided on and cannot be overtaken.
        var anchor = await dbContext.ProductConfigurationDocuments
            .SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
        if (anchor is null)
        {
            // No anchor is the valid sparse state of a Workspace that has never committed a
            // configuration change. The first mutation materialises it at the new revision; a read
            // still never creates it.
            dbContext.ProductConfigurationDocuments.Add(
                new ProductConfigurationDocumentRecord(workspaceId, newRevision));
        }
        else
        {
            // Advance rather than assign: the concurrency token on Revision then turns a lost update
            // into a version conflict instead of overwriting a revision another command committed.
            anchor.Advance();
            // The response ETag has to describe exactly what this transaction writes. Under the update
            // lock the anchor cannot have moved since the caller decided, so a mismatch is an
            // integrity fault and fails the command closed rather than serving a validator for a
            // revision that was never committed.
            if (anchor.Revision != newRevision)
                throw new InvalidOperationException("The Product Configuration revision moved inside the mutation transaction.");
        }

        var existing = await dbContext.ProductConfigurationTypeOverrides.SingleOrDefaultAsync(
            item => item.WorkspaceId == workspaceId && item.ProductTypeCode == productTypeCode,
            cancellationToken);
        if (overrideStatus is null)
        {
            // Restoring the canonical default removes the deviation row. Keeping an explicit ACTIVE
            // row would persist "an override exists" as though it were business state, which Model B
            // forbids, and the effective document is identical either way.
            if (existing is not null)
                dbContext.ProductConfigurationTypeOverrides.Remove(existing);
            return;
        }

        if (existing is null)
        {
            dbContext.ProductConfigurationTypeOverrides.Add(
                new ProductConfigurationTypeOverride(workspaceId, productTypeCode, overrideStatus));
        }
        else
        {
            existing.SetStatus(overrideStatus);
        }
    }

    private async Task<ProductConfigurationState> QueryProductConfigurationAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        var anchor = await dbContext.ProductConfigurationDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
        var overrides = await dbContext.ProductConfigurationTypeOverrides
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .ToArrayAsync(cancellationToken);
        var trusted = await dbContext.ProductConfigurationTrustedRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId, cancellationToken);
        // No anchor is the valid sparse state of a Workspace that has never committed a
        // configuration change. It reports revision 0 and is never created by this read.
        // No trusted row means nothing has been served yet, which is the same as a trusted 0.
        return new ProductConfigurationState(
            anchor?.Revision ?? 0L,
            trusted?.GreatestTrustedRevision ?? 0L,
            overrides);
    }

    /// <summary>
    /// Commits the success evidence of one configuration disclosure: the read audit record and, when
    /// the served revision exceeds it, the raised trusted-revision mark. Both commit together, so the
    /// server can never report success for a revision whose trust evidence was not established -
    /// which is exactly what a later rollback check depends on.
    /// </summary>
    public async Task RecordConfigurationReadEvidenceAsync(
        string workspaceId,
        long servedRevision,
        ProductAuditRecord audit,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await RaiseProductConfigurationTrustAsync(workspaceId, servedRevision, cancellationToken);
        dbContext.AuditRecords.Add(audit);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// The single monotonic trust-raising statement, shared by the public read and by Product
    /// commands. It joins whatever transaction the caller already has, so there is exactly one
    /// trust-write implementation and no weaker duplicate can drift into command code.
    /// </summary>
    public Task RaiseProductConfigurationTrustAsync(
        string workspaceId,
        long revision,
        CancellationToken cancellationToken) =>
        // One atomic monotonic upsert rather than a read-modify-write: a slower request that relied on
        // a lower revision can never lower a mark another request already raised.
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             MERGE [products].[ProductConfigurationTrustedRevisions] WITH (HOLDLOCK) AS target
             USING (SELECT {workspaceId} AS WorkspaceId, {revision} AS GreatestTrustedRevision) AS source
             ON target.[WorkspaceId] = source.[WorkspaceId]
             WHEN MATCHED AND target.[GreatestTrustedRevision] < source.[GreatestTrustedRevision]
                 THEN UPDATE SET target.[GreatestTrustedRevision] = source.[GreatestTrustedRevision]
             WHEN NOT MATCHED AND source.[GreatestTrustedRevision] > 0
                 THEN INSERT ([WorkspaceId], [GreatestTrustedRevision])
                      VALUES (source.[WorkspaceId], source.[GreatestTrustedRevision]);
             """,
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
