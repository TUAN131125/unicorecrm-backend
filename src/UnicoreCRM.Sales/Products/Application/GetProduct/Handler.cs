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
        var access = await authorization.AuthorizeAsync(
            ProductCapabilities.Read,
            query.Metadata.CorrelationId,
            cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<ProductDocument>.Failure(access.Error!);
        if (!ProductValidation.IsEntityId(query.ProductId))
            return ProductOperationResult<ProductDocument>.Failure(ProductErrors.NotFound());

        var ownership = ProductResource.ValidateOwned(
            await persistence.ReadProductAsync(query.ProductId, cancellationToken),
            access.Value!);
        return ownership.IsSuccess
            ? ProductOperationResult<ProductDocument>.Success(ProductProjection.Document(ownership.Value!))
            : ProductOperationResult<ProductDocument>.Failure(ownership.Error!);
    }
}
