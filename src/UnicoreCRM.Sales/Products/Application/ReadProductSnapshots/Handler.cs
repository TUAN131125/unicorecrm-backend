using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.ReadProductSnapshots;

/// <summary>
/// The Products-owned snapshot reader. Products decides resolution and eligibility here; a consumer
/// never interprets Product lifecycle or status for itself, and never sees Products persistence.
///
/// It maps no route. It is not <c>listProducts</c> or <c>getProduct</c> and returns no
/// <c>ProductDocument</c>: only the six frozen capture facts.
/// </summary>
internal sealed class Handler(
    ProductAuthorization authorization,
    IProductsPersistence persistence) : IProductSnapshotReader
{
    private const string Operation = "readProductSnapshots";

    /// <summary>The only capturable status. Every other value is a Products-owned eligibility refusal.</summary>
    private const string CapturableStatus = "ACTIVE";

    public async Task<ProductSnapshotReadResult> ResolveAsync(
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
            return new ProductSnapshotReadResult(true, []);

        var metadata = new ProductRequestMetadata(Operation, Operation);
        var access = await authorization.AuthorizeAsync(ProductCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
        {
            // No entry at all, so a consumer refused products.read learns nothing about any Product -
            // not even whether the identifiers it supplied exist.
            return ProductSnapshotReadResult.Denied();
        }

        // AccessControl resolves the record scope once; there are no per-row decisions. Product has
        // no member-owner concept, so a restrictive policy resolves uniformly to "no Product is
        // visible" and every identifier is indistinguishably unresolvable rather than unfiltered.
        if (access.Value!.Authorization.ScopeFilter != RecordAccessScopeFilter.Workspace)
            return new ProductSnapshotReadResult(true, [.. productIds.Select(NotResolvable)]);

        var distinct = productIds.Distinct(StringComparer.Ordinal).ToArray();
        var found = await persistence.ReadProductSnapshotsAsync(
            access.Value.Trusted.WorkspaceId,
            distinct,
            cancellationToken);
        var byId = found.ToDictionary(product => product.ProductId, StringComparer.Ordinal);

        var entries = new List<ProductSnapshotEntry>(distinct.Length);
        foreach (var productId in distinct)
        {
            // A Product of another Workspace is never loaded, because the trusted Workspace is a
            // query predicate rather than a post-materialisation check. Unknown and foreign are
            // therefore the same outcome by construction, not by a comparison that could drift.
            if (!byId.TryGetValue(productId, out var product))
            {
                entries.Add(NotResolvable(productId));
                continue;
            }

            entries.Add(string.Equals(product.Profile.Status, CapturableStatus, StringComparison.Ordinal)
                ? new ProductSnapshotEntry(productId, ProductSnapshotOutcome.Resolved, Facts(product))
                : new ProductSnapshotEntry(productId, ProductSnapshotOutcome.NotEligible, null));
        }

        return new ProductSnapshotReadResult(true, entries);
    }

    /// <summary>
    /// The frozen six-field projection. No price, tax, billing, description, category, unit, tag or
    /// archive fact crosses this boundary.
    /// </summary>
    private static ProductSnapshotFacts Facts(Product product) =>
        new(
            product.ProductId,
            product.Profile.Name,
            product.Profile.Sku,
            product.Profile.Type,
            product.Profile.Status,
            product.Version);

    private static ProductSnapshotEntry NotResolvable(string productId) =>
        new(productId, ProductSnapshotOutcome.NotResolvable, null);
}
