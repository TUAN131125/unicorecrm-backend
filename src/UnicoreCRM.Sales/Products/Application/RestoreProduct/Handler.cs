using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.RestoreProduct;

internal sealed record Command(string ProductId, RestoreProductRequest Request, ProductCommandMetadata Metadata);

internal sealed class Handler(ProductAuthorization authorization, ProductMutationExecution execution)
{
    internal async Task<ProductOperationResult<ProductMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new ProductRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(ProductCapabilities.Edit, metadata, cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<ProductMutationResponse>.Failure(access.Error!);
        if (!ProductValidation.IsEntityId(command.ProductId))
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.NotFound());

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var reason = ProductValidation.OptionalText(command.Request.Reason, "reason", 1000, fields);
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
            "restoreProduct",
            "PRODUCT_RESTORED",
            command.ProductId,
            command.Metadata,
            fingerprint,
            (product, now) => product.Restore(now) ? null : ProductErrors.RestoreBlocked(product.ProductId),
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "restoreProduct", metadata, cancellationToken),
            recordAccess => ProductAuthorization.EnforceFieldWrite(recordAccess, "status", "archivedAt", "archiveReason"),
            cancellationToken);
    }
}
