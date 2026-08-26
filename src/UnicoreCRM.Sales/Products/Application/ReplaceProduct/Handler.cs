using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.ReplaceProduct;

internal sealed record Command(string ProductId, ReplaceProductRequest Request, ProductCommandMetadata Metadata);

internal sealed class Handler(
    ProductAuthorization authorization,
    IProductsPersistence persistence,
    IWorkspaceCurrencyConfigurationReader currencyReader,
    TimeProvider timeProvider)
{
    internal async Task<ProductOperationResult<ProductMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(
            ProductCapabilities.Edit,
            command.Metadata.CorrelationId,
            cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<ProductMutationResponse>.Failure(access.Error!);
        if (!ProductValidation.IsEntityId(command.ProductId))
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.NotFound());

        var trusted = access.Value!;
        var currency = await currencyReader.FindAsync(trusted.WorkspaceId, cancellationToken);
        if (currency is null)
        {
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.PricingInvalid(
                new Dictionary<string, string[]> { ["unitPrice.currency"] = ["Workspace currency configuration is unavailable."] }));
        }

        ProductValidation.TryProfile(
            command.Request,
            currency.BaseCurrency,
            out var profile,
            out var fields,
            out var pricingFields);
        if (fields.Count != 0)
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.Validation(fields));
        if (pricingFields.Count != 0)
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.PricingInvalid(pricingFields));

        var fingerprint = ProductCommandSupport.Fingerprint(new
        {
            command.ProductId,
            Profile = profile,
            currency.ConfigurationVersion,
            command.Metadata.ExpectedVersion
        });
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = ProductCommandSupport.ScopeKey(trusted, "replaceProduct", command.ProductId, command.Metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = ProductCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? ProductOperationResult<ProductMutationResponse>.Success(ProductCommandSupport.Replay(existing))
                : ProductOperationResult<ProductMutationResponse>.Failure(replayError);
        }

        var ownership = ProductResource.ValidateOwned(
            await persistence.LoadProductAsync(command.ProductId, cancellationToken),
            trusted);
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
        return ProductOperationResult<ProductMutationResponse>.Success(response);
    }
}
