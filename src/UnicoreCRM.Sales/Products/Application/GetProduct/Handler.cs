using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.GetProduct;

internal sealed record Query(string ProductId, ProductRequestMetadata Metadata);

internal sealed class Handler(ProductAuthorization authorization, IProductsPersistence persistence)
{
    internal async Task<ProductOperationResult<ProductDocument>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var metadata = new ProductRequestMetadata(query.Metadata.RequestId, query.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(ProductCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<ProductDocument>.Failure(access.Error!);
        if (!ProductValidation.IsEntityId(query.ProductId))
            return ProductOperationResult<ProductDocument>.Failure(ProductErrors.NotFound());

        var ownership = ProductResource.Resolve(
            await persistence.ReadProductAsync(access.Value!.Trusted.WorkspaceId, query.ProductId, cancellationToken));
        if (!ownership.IsSuccess)
            return ProductOperationResult<ProductDocument>.Failure(ownership.Error!);

        // Record scope is enforced here, not left to the consumer. Product has no member owner, so
        // an OWN policy denies every Product and the record is reported as not found.
        var denied = await authorization.EnforceRecordAsync(
            access.Value!, ownership.Value!, "getProduct", metadata, cancellationToken);
        if (denied is not null)
            return ProductOperationResult<ProductDocument>.Failure(denied);

        return ProductOperationResult<ProductDocument>.Success(
            ProductFieldSecurity.Project(ProductProjection.Document(ownership.Value!), access.Value!.Authorization));
    }
}
