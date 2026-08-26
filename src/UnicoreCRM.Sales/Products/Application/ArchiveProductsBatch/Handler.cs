using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.ArchiveProductsBatch;

internal sealed record Command(ArchiveProductsBatchRequest Request, ProductCommandMetadata Metadata);

internal sealed class Handler(ProductBatchMutationExecution execution)
{
    internal Task<ProductOperationResult<ProductBatchMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken) =>
        execution.ExecuteAsync(
            command.Request.Items,
            command.Request.Reason,
            command.Metadata,
            ProductBatchMutationKind.Archive,
            cancellationToken);
}
