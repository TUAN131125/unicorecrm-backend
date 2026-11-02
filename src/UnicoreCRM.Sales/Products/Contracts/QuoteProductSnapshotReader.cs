namespace UnicoreCRM.Sales.Products.Contracts;

/// <summary>
/// Products-owned commercial capture boundary for Quote Core. The reader authorizes the current
/// actor and trusted Workspace; omitted identifiers are deliberately indistinguishable across
/// missing, foreign, inaccessible and ineligible Products. It maps no public route.
/// </summary>
public interface IQuoteProductSnapshotReader
{
    /// <summary>Reads at most 500 identifiers from authoritative Products persistence in one batch.</summary>
    Task<QuoteProductSnapshotResult> ReadAsync(
        IReadOnlyCollection<string> productIds,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed record QuoteProductSnapshot(
    string ProductId,
    string Sku,
    string Name,
    string Type,
    string? Description,
    string BillingCycle,
    ProductMoney UnitPrice,
    string TaxRate,
    string TaxMode,
    long Version);

public sealed record QuoteProductSnapshotResult(
    bool IsAuthorized,
    IReadOnlyDictionary<string, QuoteProductSnapshot> Items);
