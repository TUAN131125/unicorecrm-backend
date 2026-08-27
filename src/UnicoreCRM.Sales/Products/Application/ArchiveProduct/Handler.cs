using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.ArchiveProduct;

internal sealed record Command(string ProductId, ArchiveProductRequest Request, ProductCommandMetadata Metadata);

internal sealed class Handler(ProductAuthorization authorization, ProductMutationExecution execution)
{
    internal async Task<ProductOperationResult<ProductMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new ProductRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(ProductCapabilities.Delete, metadata, cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<ProductMutationResponse>.Failure(access.Error!);
        if (!ProductValidation.IsEntityId(command.ProductId))
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.NotFound());

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var reason = ProductValidation.RequiredText(command.Request.Reason, "reason", 1000, fields);
        if (fields.Count != 0)
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.Validation(fields));
        var fingerprint = ProductCommandSupport.Fingerprint(new
        {
            command.ProductId,
            Reason = reason,
            command.Metadata.ExpectedVersion
        });
        return await execution.ExecuteAsync(
            access.Value!,
            "archiveProduct",
            "PRODUCT_ARCHIVED",
            command.ProductId,
            command.Metadata,
            fingerprint,
            (product, now) => product.Archive(reason!, now) ? null : ProductErrors.ArchiveBlocked(product.ProductId),
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "archiveProduct", metadata, cancellationToken, "status", "archivedAt", "archiveReason"),
            cancellationToken);
    }
}
