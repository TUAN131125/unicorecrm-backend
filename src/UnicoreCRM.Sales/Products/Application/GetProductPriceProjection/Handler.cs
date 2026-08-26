using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.GetProductPriceProjection;

internal sealed record Query(string ProductId, string? Quantity, ProductRequestMetadata Metadata);

internal sealed class Handler(
    ProductAuthorization authorization,
    IProductsPersistence persistence,
    IWorkspaceCurrencyConfigurationReader currencyReader,
    TimeProvider timeProvider)
{
    internal async Task<ProductOperationResult<ProductPriceProjectionReadModel>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(
            ProductCapabilities.Read,
            query.Metadata.CorrelationId,
            cancellationToken);
        if (!access.IsSuccess)
            return ProductOperationResult<ProductPriceProjectionReadModel>.Failure(access.Error!);
        if (!ProductValidation.IsEntityId(query.ProductId))
            return ProductOperationResult<ProductPriceProjectionReadModel>.Failure(ProductErrors.NotFound());
        if (!ProductDecimal.TryParse(query.Quantity, out var quantity) || quantity.IsNegative || quantity.IsZero)
        {
            return ProductOperationResult<ProductPriceProjectionReadModel>.Failure(ProductErrors.PricingInvalid(
                new Dictionary<string, string[]> { ["quantity"] = ["quantity must be a positive decimal string with at most six fractional digits."] }));
        }

        var trusted = access.Value!;
        var ownership = ProductResource.ValidateOwned(
            await persistence.ReadProductAsync(query.ProductId, cancellationToken),
            trusted);
        if (!ownership.IsSuccess)
            return ProductOperationResult<ProductPriceProjectionReadModel>.Failure(ownership.Error!);

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

        var subtotal = ProductDecimal.Multiply(unitPrice, quantity);
        var hundred = new ProductDecimal(100, 0);
        var taxAmount = product.Profile.TaxMode switch
        {
            "exclusive" => ProductDecimal.Divide(ProductDecimal.Multiply(subtotal, taxRate), hundred),
            "inclusive" when !taxRate.IsZero => ProductDecimal.Add(
                subtotal,
                Negate(ProductDecimal.Divide(ProductDecimal.Multiply(subtotal, hundred), ProductDecimal.Add(hundred, taxRate)))),
            _ => default
        };
        var total = product.Profile.TaxMode == "exclusive" ? ProductDecimal.Add(subtotal, taxAmount) : subtotal;
        var moneyCurrency = product.Profile.UnitPrice.Currency;
        var evaluatedAt = ProductProjection.Utc(timeProvider.GetUtcNow());
        return ProductOperationResult<ProductPriceProjectionReadModel>.Success(new(
            product.ProductId,
            quantity.ToString(),
            new ProductMoney(unitPrice.ToString(), moneyCurrency),
            new ProductMoney(subtotal.ToString(), moneyCurrency),
            new ProductMoney(taxAmount.ToString(), moneyCurrency),
            new ProductMoney(total.ToString(), moneyCurrency),
            $"product-{product.Version}-workspace-{currency.ConfigurationVersion}",
            evaluatedAt));
    }

    private static ProductDecimal Negate(ProductDecimal value) => new(-value.Unscaled, value.Scale);
}
