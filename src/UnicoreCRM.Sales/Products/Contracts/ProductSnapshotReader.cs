namespace UnicoreCRM.Sales.Products.Contracts;

/// <summary>
/// The narrow Products-owned snapshot reader frozen by
/// <c>DEC-PRODUCTS-LEAD-INTERESTED-PRODUCT-SNAPSHOT</c>. It exposes six Product facts and nothing
/// else: no <c>ProductDocument</c>, no persistence, and deliberately no price, tax or billing fact,
/// so it does not pre-empt the commercial snapshot questions that remain open for Deals, Quotes and
/// Orders.
///
/// It is an internal owner boundary in the spirit of <c>IEffectiveWorkspaceBaseCurrencyReader</c>:
/// it maps no route and widens no public Products surface.
/// </summary>
public interface IProductSnapshotReader
{
    /// <summary>
    /// Resolves a set of distinct Product identifiers in one owner-local batch read.
    /// The consumer supplies no expected version; the current version is returned as capture
    /// provenance.
    /// </summary>
    Task<ProductSnapshotReadResult> ResolveAsync(
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken);
}

public enum ProductSnapshotOutcome
{
    /// <summary>Resolved in the trusted Workspace and capturable.</summary>
    Resolved,

    /// <summary>
    /// Unknown, foreign-Workspace, or structurally invalid. The three are deliberately one outcome:
    /// a consumer must never learn from a resolution attempt that a Product exists elsewhere.
    /// </summary>
    NotResolvable,

    /// <summary>
    /// Resolved in the trusted Workspace but not <c>ACTIVE</c>. Distinguishable from
    /// <see cref="NotResolvable"/> on purpose: the caller holds <c>products.read</c>, so status is
    /// already readable through <c>getProduct</c> and separating the two discloses nothing new.
    /// </summary>
    NotEligible
}

/// <summary>
/// The frozen six-field capture projection. Every field is either required by the consuming Lead
/// contract or is the version provenance the snapshot authority requires.
/// </summary>
public sealed record ProductSnapshotFacts(
    string ProductId,
    string Name,
    string Sku,
    string ProductType,
    string Status,
    long Version);

public sealed record ProductSnapshotEntry(
    string ProductId,
    ProductSnapshotOutcome Outcome,
    ProductSnapshotFacts? Facts);

/// <param name="IsAuthorized">
/// False when <c>products.read</c> was refused at the Products application boundary. No entry is
/// returned in that case, so a denied consumer learns nothing about any Product.
/// </param>
public sealed record ProductSnapshotReadResult(
    bool IsAuthorized,
    IReadOnlyList<ProductSnapshotEntry> Entries)
{
    public static ProductSnapshotReadResult Denied() => new(false, []);
}
