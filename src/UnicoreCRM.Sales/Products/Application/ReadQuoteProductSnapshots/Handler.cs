using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Sales.Products.Application.ReadQuoteProductSnapshots;

internal sealed class Handler(
    IRecordAccessEvaluator evaluator,
    IProductsPersistence persistence) : IQuoteProductSnapshotReader
{
    private const string Operation = "readQuoteProductSnapshots";
    private static readonly string[] CapturedFields =
        ["id", "sku", "name", "type", "description", "billingCycle", "unitPrice", "taxRate", "taxMode", "version"];
    private static readonly RecordAccessRepresentation Representation =
        RecordAccessRepresentation.Create("quoteProductSnapshot");

    public async Task<QuoteProductSnapshotResult> ReadAsync(
        IReadOnlyCollection<string> productIds,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        if (productIds.Count > 500 || productIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 160))
            throw new ArgumentException("Provide at most 500 nonempty Product identifiers of at most 160 characters.", nameof(productIds));
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        cancellationToken.ThrowIfCancellationRequested();

        var context = new RecordAccessRequestContext(requestId, correlationId);
        var access = await evaluator.AuthorizeResourceAsync(
            ProductAuthorization.ResourceKey, ProductCapabilities.Read.Capability,
            CapturedFields, Representation, context, cancellationToken);

        // Description is optional in the public Product model but every captured value must be
        // readable here. Never silently replace a hidden value with a seemingly authoritative null.
        if (!access.IsAllowed || access.TrustedWorkspace is null
            || access.UnenforceableFieldKeys.Count != 0 || CapturedFields.Any(field => !access.CanRead(field)))
            return new QuoteProductSnapshotResult(false, new Dictionary<string, QuoteProductSnapshot>(StringComparer.Ordinal));

        var items = new Dictionary<string, QuoteProductSnapshot>(StringComparer.Ordinal);
        // Products has no member owner; the owner's existing scope contract therefore has no
        // eligible records for a non-Workspace filter. Do not materialize out-of-scope Products.
        if (productIds.Count == 0 || access.ScopeFilter != RecordAccessScopeFilter.Workspace)
            return new QuoteProductSnapshotResult(true, items);

        var products = await persistence.ReadProductSnapshotsAsync(
            access.TrustedWorkspace.WorkspaceId, productIds.Distinct(StringComparer.Ordinal).ToArray(), cancellationToken);
        foreach (var product in products)
        {
            var decision = await evaluator.AuthorizeRecordAsync(
                access, product.ProductId, ProductAuthorization.Facts(product), Operation, context, cancellationToken);
            if (!decision.IsAllowed || product.IsArchived || product.Profile.Status != "ACTIVE")
                continue;

            var profile = product.Profile;
            items.Add(product.ProductId, new QuoteProductSnapshot(
                product.ProductId, profile.Sku, profile.Name, profile.Type, profile.Description,
                profile.BillingCycle, new ProductMoney(profile.UnitPrice.Amount, profile.UnitPrice.Currency),
                profile.TaxRate, profile.TaxMode, product.Version));
        }

        return new QuoteProductSnapshotResult(true, items);
    }
}
