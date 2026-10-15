using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.SearchProductSelections;

internal sealed record Query(string? Search, int Limit, string? Cursor, IReadOnlyList<string>? Ids, ProductRequestMetadata Metadata);

internal sealed class Handler(ProductAuthorization authorization, IProductsPersistence persistence,
    ProductSelectionCursor cursor, TimeProvider clock)
{
    internal async Task<ProductOperationResult<ProductSelectionPage>> HandleAsync(Query query, CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(ProductCapabilities.Read, query.Metadata, cancellationToken);
        if (!access.IsSuccess) return ProductOperationResult<ProductSelectionPage>.Failure(access.Error!);
        var search = query.Search?.Trim() ?? string.Empty;
        var fields = new Dictionary<string, string[]>();
        if (query.Limit is < 1 or > 50) fields["limit"] = ["limit must be between 1 and 50."];
        if (search.Length > 160) fields["q"] = ["q must contain at most 160 characters."];
        if (query.Ids is { } ids && (ids.Count is < 1 or > 100 || ids.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 160)
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)) fields["ids"] = ["Provide between 1 and 100 unique Product IDs."];
        if (query.Ids is not null && (query.Search is not null || query.Cursor is not null)) fields["ids"] = ["ids cannot be combined with q or cursor."];
        if (fields.Count != 0) return ProductOperationResult<ProductSelectionPage>.Failure(ProductErrors.Validation(fields, 400));

        var granted = access.Value!;
        var binding = ProductSelectionCursor.Bind(granted, search, query.Limit);
        ProductSelectionContinuation? continuation = null;
        if (query.Cursor is not null && !cursor.TryDecode(query.Cursor, binding, out continuation))
            return ProductOperationResult<ProductSelectionPage>.Failure(ProductErrors.Validation(new Dictionary<string, string[]> { ["cursor"] = ["cursor is invalid or expired."] }, 400));

        IReadOnlyList<ProductSelectionRow> rows = [];
        if (granted.Authorization.ScopeFilter == RecordAccessScopeFilter.Workspace)
        {
            rows = query.Ids is not null
                ? await persistence.ResolveProductSelectionsAsync(granted.Trusted.WorkspaceId, query.Ids, cancellationToken)
                : await persistence.SearchProductSelectionsAsync(granted.Trusted.WorkspaceId, search, search.ToUpperInvariant(), query.Limit + 1,
                    continuation?.CreatedAt, continuation?.Id, cancellationToken);
        }
        var hasNext = query.Ids is null && rows.Count > query.Limit;
        var page = hasNext ? rows.Take(query.Limit).ToArray() : rows.ToArray();
        var response = new ProductSelectionPage(page.Select(item => new ProductSelectionItem(item.Id, item.Name, item.Sku, item.Status, item.Version)).ToArray(),
            new ProductSelectionPageInfo(hasNext, hasNext ? cursor.Encode(page[^1], binding) : null));
        await ProductReadAudit.RecordCanonicalAsync(persistence, null, null, granted.Trusted, query.Metadata,
            "searchProductSelections", clock.GetUtcNow(), cancellationToken);
        return ProductOperationResult<ProductSelectionPage>.Success(response);
    }
}
