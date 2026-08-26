namespace UnicoreCRM.Sales.Products.Application.Common;

internal sealed record ProductPricingCalculation(
    ProductDecimal Subtotal,
    ProductDecimal TaxAmount,
    ProductDecimal Total);

internal static class ProductPricingCalculator
{
    internal const int MaximumScale = 6;

    internal static ProductPricingCalculation Calculate(
        ProductDecimal unitPrice,
        ProductDecimal quantity,
        ProductDecimal taxRate,
        string taxMode)
    {
        var exactSubtotal = ProductDecimal.Multiply(unitPrice, quantity);
        var subtotal = ProductDecimal.RoundHalfUp(exactSubtotal, MaximumScale);
        var hundred = new ProductDecimal(100, 0);

        return taxMode switch
        {
            "exclusive" => CalculateExclusive(exactSubtotal, subtotal, taxRate, hundred),
            "inclusive" => CalculateInclusive(exactSubtotal, subtotal, taxRate, hundred),
            _ => new ProductPricingCalculation(subtotal, default, subtotal)
        };
    }

    private static ProductPricingCalculation CalculateExclusive(
        ProductDecimal exactSubtotal,
        ProductDecimal subtotal,
        ProductDecimal taxRate,
        ProductDecimal hundred)
    {
        var exactTaxNumerator = ProductDecimal.Multiply(exactSubtotal, taxRate);
        var taxAmount = ProductDecimal.DivideAndRoundHalfUp(exactTaxNumerator, hundred, MaximumScale);
        var total = ProductDecimal.RoundHalfUp(ProductDecimal.Add(subtotal, taxAmount), MaximumScale);
        return new ProductPricingCalculation(subtotal, taxAmount, total);
    }

    private static ProductPricingCalculation CalculateInclusive(
        ProductDecimal exactSubtotal,
        ProductDecimal subtotal,
        ProductDecimal taxRate,
        ProductDecimal hundred)
    {
        if (taxRate.IsZero)
            return new ProductPricingCalculation(subtotal, default, subtotal);

        var exactTaxNumerator = ProductDecimal.Multiply(exactSubtotal, taxRate);
        var taxAmount = ProductDecimal.DivideAndRoundHalfUp(
            exactTaxNumerator,
            ProductDecimal.Add(hundred, taxRate),
            MaximumScale);
        return new ProductPricingCalculation(subtotal, taxAmount, subtotal);
    }
}
