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
        var metadata = new ProductRequestMetadata(query.Metadata.RequestId, query.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(ProductCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<ProductAvailabilityReadModel>.Failure(access.Error!);
        if (!ProductValidation.IsEntityId(query.ProductId))
            return ProductOperationResult<ProductAvailabilityReadModel>.Failure(ProductErrors.NotFound());

        var ownership = ProductResource.ValidateOwned(
            await persistence.ReadProductAsync(query.ProductId, cancellationToken),
            access.Value!.Trusted);
        if (!ownership.IsSuccess)
            return ProductOperationResult<ProductAvailabilityReadModel>.Failure(ownership.Error!);

        // Record scope is enforced before any projection is computed, so a Product the caller's
        // record scope hides is reported as not found rather than answered.
        var denied = await authorization.EnforceRecordAsync(
            access.Value!, ownership.Value!, "getProductAvailability", metadata, cancellationToken);
        if (denied is not null)
            return ProductOperationResult<ProductAvailabilityReadModel>.Failure(denied);

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
        var now = timeProvider.GetUtcNow();
        var response = new ProductAvailabilityReadModel(
            product.ProductId,
            status == "AVAILABLE",
            status,
            blockers,
            product.Version,
            ProductProjection.Utc(now));
        await ProductReadAudit.RecordAsync(
            persistence,
            product,
            access.Value!.Trusted,
            query.Metadata,
            "getProductAvailability",
            now,
            cancellationToken);
        return ProductOperationResult<ProductAvailabilityReadModel>.Success(response);
    }
}
