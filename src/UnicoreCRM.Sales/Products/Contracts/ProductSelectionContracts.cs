using System.Text.Json.Serialization;
namespace UnicoreCRM.Sales.Products.Contracts;

public sealed record ProductSelectionItem(string Id, string Name, string Sku, string Status, long Version);
public sealed record ProductSelectionPageInfo(bool HasNextPage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor = null);
public sealed record ProductSelectionPage(IReadOnlyList<ProductSelectionItem> Items, ProductSelectionPageInfo PageInfo);
