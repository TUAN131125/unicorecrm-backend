using System.Globalization;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.Common;

internal static class ProductProjection
{
    internal static ProductDocument Document(Product product)
    {
        var profile = product.Profile;
        return new ProductDocument(
            product.ProductId,
            profile.Sku,
            profile.Name,
            profile.Type,
            profile.Status,
            profile.Category,
            profile.Unit,
            Money(profile.UnitPrice),
            profile.TaxRate,
            profile.TaxMode,
            profile.BillingCycle,
            profile.IsSubscription,
            profile.IsRenewable,
            profile.Tags,
            product.Version,
            Utc(product.CreatedAt),
            Utc(product.UpdatedAt))
        {
            Description = profile.Description,
            CostPrice = profile.CostPrice is null ? null : Money(profile.CostPrice),
            WarrantyMonths = profile.WarrantyMonths,
            DefaultContractMonths = profile.DefaultContractMonths,
            ArchivedAt = product.ArchivedAt is null ? null : Utc(product.ArchivedAt.Value),
            ArchiveReason = product.ArchiveReason
        };
    }

    internal static ProductMoney Money(ProductMoneyValue value) => new(value.Amount, value.Currency);

    internal static string Utc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
