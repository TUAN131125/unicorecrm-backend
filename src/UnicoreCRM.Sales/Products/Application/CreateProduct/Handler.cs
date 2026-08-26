using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.CreateProduct;

internal sealed record Command(CreateProductRequest Request, ProductCommandMetadata Metadata);

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
        var access = await authorization.AuthorizeAsync(
            ProductCapabilities.Create,
            command.Metadata.CorrelationId,
            cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<ProductMutationResponse>.Failure(access.Error!);

        ProductValidation.TryProfile(
            command.Request,
            out var profile,
            out var fields,
            out var pricingFields);
        if (fields.Count != 0)
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.Validation(fields));
        if (pricingFields.Count != 0)
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.PricingInvalid(pricingFields));

        var trusted = access.Value!;
        var fingerprint = ProductCommandSupport.Fingerprint(new { Profile = profile });
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = ProductCommandSupport.ScopeKey(trusted, "createProduct", "WORKSPACE", command.Metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = ProductCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? ProductOperationResult<ProductMutationResponse>.Success(ProductCommandSupport.Replay(existing))
                : ProductOperationResult<ProductMutationResponse>.Failure(replayError);
        }

        var currency = await currencyReader.FindAsync(trusted.WorkspaceId, cancellationToken);
        if (currency is null)
        {
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.PricingInvalid(
                new Dictionary<string, string[]> { ["unitPrice.currency"] = ["Effective Workspace base currency is unavailable."] }));
        }
        var currencyFields = ProductValidation.ValidateEffectiveCurrency(profile!, currency.BaseCurrency);
        if (currencyFields.Count != 0)
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.PricingInvalid(currencyFields));

        if (await persistence.SkuExistsAsync(trusted.WorkspaceId, profile!.NormalizedSku, null, cancellationToken))
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.SkuConflict());

        var now = timeProvider.GetUtcNow();
        var product = new Product(trusted.WorkspaceId, profile, now);
        persistence.AddProduct(product);
        var response = ProductCommandSupport.RecordCommit(
            persistence,
            product,
            trusted,
            command.Metadata,
            "createProduct",
            "PRODUCT_CREATED",
            scopeKey,
            "WORKSPACE",
            fingerprint,
            null,
            now);
        try
        {
            await persistence.SaveChangesAsync(cancellationToken);
        }
        catch (ProductsPersistenceUniqueException)
        {
            return ProductOperationResult<ProductMutationResponse>.Failure(ProductErrors.SkuConflict());
        }
        await transaction.CommitAsync(cancellationToken);
        return ProductOperationResult<ProductMutationResponse>.Success(response);
    }
}
