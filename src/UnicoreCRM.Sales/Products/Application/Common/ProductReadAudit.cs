using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.Common;

internal static class ProductReadAudit
{
    internal static async Task RecordCanonicalAsync(
        IProductsPersistence persistence,
        string? recordId,
        long? resourceVersion,
        TrustedWorkspaceContext trusted,
        ProductRequestMetadata metadata,
        string operation,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        persistence.AddAudit(new ProductAuditRecord(
            operation,
            trusted.WorkspaceId,
            trusted.MemberId,
            recordId,
            metadata.RequestId,
            metadata.CorrelationId,
            "READ",
            null,
            resourceVersion,
            occurredAt));
        await persistence.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Records a Product Configuration disclosure. It follows the same immutable READ evidence shape
    /// as the canonical read audit, and additionally raises the Workspace's trusted revision in the
    /// same transaction. That extra write is access/integrity evidence, not a business mutation: no
    /// Product Configuration resource state is created or changed by it.
    /// </summary>
    internal static Task RecordConfigurationAsync(
        IProductsPersistence persistence,
        long servedRevision,
        TrustedWorkspaceContext trusted,
        ProductRequestMetadata metadata,
        string operation,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        persistence.RecordConfigurationReadEvidenceAsync(
            trusted.WorkspaceId,
            servedRevision,
            new ProductAuditRecord(
                operation,
                trusted.WorkspaceId,
                trusted.MemberId,
                null,
                metadata.RequestId,
                metadata.CorrelationId,
                "READ",
                null,
                servedRevision,
                occurredAt),
            cancellationToken);

    internal static async Task RecordAsync(
        IProductsPersistence persistence,
        Product product,
        TrustedWorkspaceContext trusted,
        ProductRequestMetadata metadata,
        string operation,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        persistence.AddAudit(new ProductAuditRecord(
            operation,
            trusted.WorkspaceId,
            trusted.MemberId,
            product.ProductId,
            metadata.RequestId,
            metadata.CorrelationId,
            "READ",
            null,
            null,
            occurredAt));
        await persistence.SaveChangesAsync(cancellationToken);
    }
}
