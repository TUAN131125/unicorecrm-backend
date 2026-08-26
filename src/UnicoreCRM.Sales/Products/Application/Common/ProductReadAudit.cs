using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.Common;

internal static class ProductReadAudit
{
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
