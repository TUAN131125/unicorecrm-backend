using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.Common;

internal sealed class ProductMutationExecution(
    IProductsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<ProductOperationResult<ProductMutationResponse>> ExecuteAsync(
        TrustedWorkspaceContext trusted,
        string operation,
        string eventType,
        string productId,
        ProductCommandMetadata metadata,
        string fingerprint,
        Func<Domain.Product, DateTimeOffset, ProductOperationError?> mutate,
        CancellationToken cancellationToken)
    {
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = ProductCommandSupport.ScopeKey(trusted, operation, productId, metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = ProductCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? ProductOperationResult<ProductMutationResponse>.Success(ProductCommandSupport.Replay(existing))
                : ProductOperationResult<ProductMutationResponse>.Failure(replayError);
        }

        var ownership = ProductResource.ValidateOwned(
            await persistence.LoadProductAsync(productId, cancellationToken),
            trusted);
        if (!ownership.IsSuccess)
            return ProductOperationResult<ProductMutationResponse>.Failure(ownership.Error!);

        var product = ownership.Value!;
        var expectedVersion = metadata.ExpectedVersion!.Value;
        if (product.Version != expectedVersion)
            return ProductOperationResult<ProductMutationResponse>.Failure(
                ProductErrors.VersionConflict(product.ProductId, expectedVersion, product.Version));

        var priorVersion = product.Version;
        var now = timeProvider.GetUtcNow();
        var mutationError = mutate(product, now);
        if (mutationError is not null)
            return ProductOperationResult<ProductMutationResponse>.Failure(mutationError);

        var response = ProductCommandSupport.RecordCommit(
            persistence,
            product,
            trusted,
            metadata,
            operation,
            eventType,
            scopeKey,
            productId,
            fingerprint,
            priorVersion,
            now);
        try
        {
            await persistence.SaveChangesAsync(cancellationToken);
        }
        catch (ProductsPersistenceConcurrencyException)
        {
            return ProductOperationResult<ProductMutationResponse>.Failure(
                ProductErrors.VersionConflict(product.ProductId, expectedVersion, product.Version));
        }
        await transaction.CommitAsync(cancellationToken);
        return ProductOperationResult<ProductMutationResponse>.Success(response);
    }
}
