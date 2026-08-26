using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.RestoreProductsBatch;

internal sealed record Command(RestoreProductsBatchRequest Request, ProductCommandMetadata Metadata);

internal sealed class Handler(ProductBatchMutationExecution execution)
{
    internal Task<ProductOperationResult<ProductBatchMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken) =>
        execution.ExecuteAsync(
            command.Request.Items,
            command.Request.Reason,
            command.Metadata,
            ProductBatchMutationKind.Restore,
            cancellationToken);
}
