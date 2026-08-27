using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.Common;

internal sealed class ProductMutationExecution(
    IProductsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<ProductOperationResult<ProductMutationResponse>> ExecuteAsync(
        ProductAccess access,
        string operation,
        string eventType,
        string productId,
        ProductCommandMetadata metadata,
        string fingerprint,
        Func<Domain.Product, DateTimeOffset, ProductOperationError?> mutate,
        Func<ProductAccess, Domain.Product, Task<ProductOperationError?>> recordGuard,
        Func<ProductAccess, ProductOperationError?>? fieldWriteGuard,
        CancellationToken cancellationToken)
    {
        var trusted = access.Trusted;
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);

        // The record-access guard runs before the idempotency lookup so a replay cannot bypass it.
        var guardedOwnership = ProductResource.Resolve(
            await persistence.ReadProductAsync(trusted.WorkspaceId, productId, cancellationToken));
        if (!guardedOwnership.IsSuccess)
            return ProductOperationResult<ProductMutationResponse>.Failure(guardedOwnership.Error!);
        var guardError = await recordGuard(access, guardedOwnership.Value!);
        if (guardError is not null)
            return ProductOperationResult<ProductMutationResponse>.Failure(guardError);

        var scopeKey = ProductCommandSupport.ScopeKey(trusted, operation, productId, metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = ProductCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? ProductOperationResult<ProductMutationResponse>.Success(Project(ProductCommandSupport.Replay(existing), access))
                : ProductOperationResult<ProductMutationResponse>.Failure(replayError);
        }

        // From here the command is a genuinely new execution. Field-write authorization is applied
        // for the fields this execution will actually change - never on the replay path above, which
        // writes nothing and must stay replayable after a field turns READ_ONLY or HIDDEN.
        if (fieldWriteGuard is not null)
        {
            var fieldWriteError = fieldWriteGuard(access);
            if (fieldWriteError is not null)
                return ProductOperationResult<ProductMutationResponse>.Failure(fieldWriteError);
        }

        var ownership = ProductResource.Resolve(
            await persistence.LoadProductAsync(trusted.WorkspaceId, productId, cancellationToken));
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
        return ProductOperationResult<ProductMutationResponse>.Success(Project(response, access));
    }

    /// <summary>
    /// Applies field security to the outgoing response. It is applied at the boundary because a
    /// replay returns a projection serialized under whatever policy was in force when the command
    /// committed; enforcing here makes stored evidence unable to leak a currently withheld value.
    /// </summary>
    private static ProductMutationResponse Project(ProductMutationResponse response, ProductAccess access) =>
        response with
        {
            Result = new ProductMutationResult(ProductFieldSecurity.Project(response.Result.Product, access.Authorization))
        };
}
