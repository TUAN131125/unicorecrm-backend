using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.ReplaceProduct;

internal sealed record Command(string ProductId, ReplaceProductRequest Request, ProductCommandMetadata Metadata);

internal sealed class Handler(
    ProductAuthorization authorization,
    IProductsPersistence persistence,
    IEffectiveWorkspaceBaseCurrencyReader currencyReader,
    TimeProvider timeProvider)
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

        ProductValidation.TryProfile(
            command.Request,
            out var profile,
            out var fields,
            out var pricingFields);
        if (fields.Count != 0)
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.Validation(fields));
        if (pricingFields.Count != 0)
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.PricingInvalid(pricingFields));

        var trusted = access.Value!.Trusted;
        var fingerprint = ProductCommandSupport.Fingerprint(new
        {
            command.ProductId,
            Profile = profile,
            command.Metadata.ExpectedVersion
        });
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);

        // The record-access guard runs before the idempotency lookup so a replay cannot bypass it.
        // This slice owns its own transaction rather than using ProductMutationExecution, so the
        // guard is applied here explicitly rather than being inherited.
        var guardedOwnership = ProductResource.Resolve(
            await persistence.ReadProductAsync(trusted.WorkspaceId, command.ProductId, cancellationToken));
        if (!guardedOwnership.IsSuccess)
            return ProductOperationResult<ProductMutationResponse>.Failure(guardedOwnership.Error!);
        var guardError = await authorization.EnforceRecordAsync(
            access.Value!, guardedOwnership.Value!, "replaceProduct", metadata, cancellationToken);
        if (guardError is not null)
            return ProductOperationResult<ProductMutationResponse>.Failure(guardError);

        var scopeKey = ProductCommandSupport.ScopeKey(trusted, "replaceProduct", command.ProductId, command.Metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = ProductCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? ProductOperationResult<ProductMutationResponse>.Success(Project(ProductCommandSupport.Replay(existing), access.Value!))
                : ProductOperationResult<ProductMutationResponse>.Failure(replayError);
        }

        // From here the command is a genuinely new execution. The requested profile is compared
        // against the stored one, so only a field the replacement actually changes is treated as a
        // write; repeating a READ_ONLY value unchanged is not a write and is not refused. It follows
        // the record guard, so a Product the caller may not reach is reported as missing rather than
        // leaking a field-policy refusal, and it follows the replay branch, so a committed
        // replacement stays replayable after a field turns READ_ONLY or HIDDEN.
        var writeError = ProductFieldSecurity.GuardProfileWrite(
            access.Value!.Authorization, guardedOwnership.Value!.Profile, profile!);
        if (writeError is not null)
            return ProductOperationResult<ProductMutationResponse>.Failure(writeError);

        var currency = await currencyReader.FindAsync(trusted.WorkspaceId, cancellationToken);
        if (currency is null)
        {
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.PricingInvalid(
                new Dictionary<string, string[]> { ["unitPrice.currency"] = ["Effective Workspace base currency is unavailable."] }));
        }
        var currencyFields = ProductValidation.ValidateEffectiveCurrency(profile!, currency.BaseCurrency);
        if (currencyFields.Count != 0)
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.PricingInvalid(currencyFields));

        var ownership = ProductResource.Resolve(
            await persistence.LoadProductAsync(trusted.WorkspaceId, command.ProductId, cancellationToken));
        if (!ownership.IsSuccess)
            return ProductOperationResult<ProductMutationResponse>.Failure(ownership.Error!);

        var product = ownership.Value!;
        var expectedVersion = command.Metadata.ExpectedVersion!.Value;
        if (product.Version != expectedVersion)
            return ProductOperationResult<ProductMutationResponse>.Failure(
                ProductErrors.VersionConflict(product.ProductId, expectedVersion, product.Version));
        if (product.IsArchived)
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.Archived(product.ProductId));
        if (await persistence.SkuExistsAsync(trusted.WorkspaceId, profile!.NormalizedSku, product.ProductId, cancellationToken))
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.SkuConflict());

        var priorVersion = product.Version;
        var now = timeProvider.GetUtcNow();
        if (!product.Replace(profile, now))
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.Archived(product.ProductId));
        var response = ProductCommandSupport.RecordCommit(
            persistence,
            product,
            trusted,
            command.Metadata,
            "replaceProduct",
            "PRODUCT_REPLACED",
            scopeKey,
            product.ProductId,
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
        catch (ProductsPersistenceUniqueException)
        {
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.SkuConflict());
        }
        await transaction.CommitAsync(cancellationToken);
        return ProductOperationResult<ProductMutationResponse>.Success(Project(response, access.Value!));
    }

    private static ProductMutationResponse Project(ProductMutationResponse response, ProductAccess access) =>
        response with
        {
            Result = new ProductMutationResult(ProductFieldSecurity.Project(response.Result.Product, access.Authorization))
        };
}
