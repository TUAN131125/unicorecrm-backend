using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.GetProductPriceProjection;

internal sealed record Query(string ProductId, string? Quantity, ProductRequestMetadata Metadata);

internal sealed class Handler(
    ProductAuthorization authorization,
    IProductsPersistence persistence,
    IEffectiveWorkspaceBaseCurrencyReader currencyReader,
    TimeProvider timeProvider)
{
    internal async Task<ProductOperationResult<ProductPriceProjectionReadModel>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var metadata = new ProductRequestMetadata(query.Metadata.RequestId, query.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(ProductCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<ProductPriceProjectionReadModel>.Failure(access.Error!);
        if (!ProductValidation.IsEntityId(query.ProductId))
            return ProductOperationResult<ProductPriceProjectionReadModel>.Failure(ProductErrors.NotFound());
        if (!ProductDecimal.TryParse(query.Quantity, out var quantity) || quantity.IsNegative || quantity.IsZero)
        {
            return ProductOperationResult<ProductPriceProjectionReadModel>.Failure(ProductErrors.PricingInvalid(
                new Dictionary<string, string[]> { ["quantity"] = ["quantity must be a positive decimal string with at most six fractional digits."] }));
        }

        var trusted = access.Value!.Trusted;
        var ownership = ProductResource.ValidateOwned(
            await persistence.ReadProductAsync(query.ProductId, cancellationToken),
            trusted);
        if (!ownership.IsSuccess)
            return ProductOperationResult<ProductPriceProjectionReadModel>.Failure(ownership.Error!);

        // Record scope is enforced before any projection is computed, so a Product the caller's
        // record scope hides is reported as not found rather than answered.
        var denied = await authorization.EnforceRecordAsync(
            access.Value!, ownership.Value!, "getProductPriceProjection", metadata, cancellationToken);
        if (denied is not null)
            return ProductOperationResult<ProductPriceProjectionReadModel>.Failure(denied);

        var product = ownership.Value!;
        var expectedVersion = query.Metadata.ExpectedVersion!.Value;
        if (product.Version != expectedVersion)
            return ProductOperationResult<ProductPriceProjectionReadModel>.Failure(
                ProductErrors.VersionConflict(product.ProductId, expectedVersion, product.Version));

        var currency = await currencyReader.FindAsync(trusted.WorkspaceId, cancellationToken);
        if (currency is null || !string.Equals(currency.BaseCurrency, product.Profile.UnitPrice.Currency, StringComparison.Ordinal))
        {
            return ProductOperationResult<ProductPriceProjectionReadModel>.Failure(ProductErrors.PricingInvalid(
                new Dictionary<string, string[]> { ["unitPrice.currency"] = ["Product currency does not match the authoritative Workspace currency."] }));
        }

        if (!ProductDecimal.TryParse(product.Profile.UnitPrice.Amount, out var unitPrice)
            || !ProductDecimal.TryParse(product.Profile.TaxRate, out var taxRate))
            throw new InvalidOperationException("Persisted Product pricing is invalid.");

        var calculation = ProductPricingCalculator.Calculate(unitPrice, quantity, taxRate, product.Profile.TaxMode);
        var moneyCurrency = product.Profile.UnitPrice.Currency;
        var now = timeProvider.GetUtcNow();
        var response = new ProductPriceProjectionReadModel(
            product.ProductId,
            quantity.ToString(),
            new ProductMoney(unitPrice.ToString(), moneyCurrency),
            new ProductMoney(calculation.Subtotal.ToString(), moneyCurrency),
            new ProductMoney(calculation.TaxAmount.ToString(), moneyCurrency),
            new ProductMoney(calculation.Total.ToString(), moneyCurrency),
            $"product-{product.Version}-effective-currency-source-{currency.SourceVersion}",
            ProductProjection.Utc(now));
        await ProductReadAudit.RecordAsync(
            persistence,
            product,
            trusted,
            query.Metadata,
            "getProductPriceProjection",
            now,
            cancellationToken);
        return ProductOperationResult<ProductPriceProjectionReadModel>.Success(response);
    }
}
