using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Sales.Products.Application.ListProducts;

internal sealed record Query(ProductRequestMetadata Metadata);

internal sealed class Handler(
    ProductAuthorization authorization,
    IProductsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<ProductOperationResult<IReadOnlyList<ProductDocument>>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var metadata = new ProductRequestMetadata(query.Metadata.RequestId, query.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(ProductCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<IReadOnlyList<ProductDocument>>.Failure(access.Error!);

        // AccessControl resolves the record scope once. Product has no member owner, so an OWN
        // policy denies every Product and the list is empty rather than unfiltered.
        var scope = access.Value!.Authorization.ScopeFilter;
        if (scope != RecordAccessScopeFilter.Workspace)
        {
            await ProductReadAudit.RecordCanonicalAsync(
                persistence,
                null,
                null,
                access.Value.Trusted,
                metadata,
                "listProducts",
                timeProvider.GetUtcNow(),
                cancellationToken);
            return ProductOperationResult<IReadOnlyList<ProductDocument>>.Success([]);
        }

        var products = await persistence.ReadProductsAsync(access.Value!.Trusted.WorkspaceId, null, cancellationToken);
        var response = products
            .Select(product => ProductFieldSecurity.Project(ProductProjection.Document(product), access.Value!.Authorization))
            .ToArray();
        await ProductReadAudit.RecordCanonicalAsync(
            persistence,
            null,
            null,
            access.Value.Trusted,
            metadata,
            "listProducts",
            timeProvider.GetUtcNow(),
            cancellationToken);
        return ProductOperationResult<IReadOnlyList<ProductDocument>>.Success(response);
    }
}
