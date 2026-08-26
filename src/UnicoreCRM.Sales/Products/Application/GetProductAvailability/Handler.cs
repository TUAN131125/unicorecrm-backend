using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.GetProductAvailability;

internal sealed record Query(string ProductId, ProductRequestMetadata Metadata);

internal sealed class Handler(
    ProductAuthorization authorization,
    IProductsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<ProductOperationResult<ProductAvailabilityReadModel>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(
            ProductCapabilities.Read,
            query.Metadata.CorrelationId,
            cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<ProductAvailabilityReadModel>.Failure(access.Error!);
        if (!ProductValidation.IsEntityId(query.ProductId))
            return ProductOperationResult<ProductAvailabilityReadModel>.Failure(ProductErrors.NotFound());

        var ownership = ProductResource.ValidateOwned(
            await persistence.ReadProductAsync(query.ProductId, cancellationToken),
            access.Value!);
        if (!ownership.IsSuccess)
            return ProductOperationResult<ProductAvailabilityReadModel>.Failure(ownership.Error!);

        var product = ownership.Value!;
        var expectedVersion = query.Metadata.ExpectedVersion!.Value;
        if (product.Version != expectedVersion)
            return ProductOperationResult<ProductAvailabilityReadModel>.Failure(
                ProductErrors.VersionConflict(product.ProductId, expectedVersion, product.Version));

        var status = product.Profile.Status switch
        {
            "ACTIVE" => "AVAILABLE",
            "INACTIVE" => "INACTIVE",
            "DRAFT" => "DRAFT",
            _ => "ARCHIVED"
        };
        var blockers = status == "AVAILABLE" ? [] : new[] { $"PRODUCT_{status}" };
        return ProductOperationResult<ProductAvailabilityReadModel>.Success(new(
            product.ProductId,
            status == "AVAILABLE",
            status,
            blockers,
            product.Version,
            ProductProjection.Utc(timeProvider.GetUtcNow())));
    }
}
