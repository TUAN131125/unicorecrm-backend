using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.ListProducts;

internal sealed record Query(ProductRequestMetadata Metadata);

internal sealed class Handler(ProductAuthorization authorization, IProductsPersistence persistence)
{
    internal async Task<ProductOperationResult<IReadOnlyList<ProductDocument>>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(
            ProductCapabilities.Read,
            query.Metadata.CorrelationId,
            cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<IReadOnlyList<ProductDocument>>.Failure(access.Error!);

        var products = await persistence.ReadProductsAsync(access.Value!.WorkspaceId, cancellationToken);
        return ProductOperationResult<IReadOnlyList<ProductDocument>>.Success(
            products.Select(ProductProjection.Document).ToArray());
    }
}
