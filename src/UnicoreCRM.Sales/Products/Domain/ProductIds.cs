namespace UnicoreCRM.Sales.Products.Domain;

internal static class ProductIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
