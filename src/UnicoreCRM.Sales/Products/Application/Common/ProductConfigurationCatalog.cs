using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.Common;

/// <summary>The persisted Product Configuration state of one Workspace, read as one snapshot.</summary>
internal sealed record ProductConfigurationState(
    long Revision,
    IReadOnlyList<ProductConfigurationTypeOverride> Overrides);

/// <summary>
/// Computes the effective Product Configuration document from the canonical ProductType vocabulary
/// and the Workspace's sparse overrides.
///
/// <para>The vocabulary is contract-global and immutable: a Workspace cannot add to it, rename it or
/// remove from it. Configuration only decorates codes that the contract already defines.</para>
/// </summary>
internal static class ProductConfigurationCatalog
{
    internal const string Active = "ACTIVE";
    internal const string Inactive = "INACTIVE";

    /// <summary>
    /// The canonical ProductType vocabulary in canonical contract order. This is the ordering
    /// authority for the read: it is neither alphabetical, nor insertion order, nor a frontend order.
    ///
    /// <para>It projects the same canonical enum that <see cref="ProductValidation"/> enforces for
    /// Product create and replace. That set is an unordered membership test and cannot supply an
    /// order, so the ordered projection lives here. Both are projections of the contract; neither is
    /// vocabulary authority.</para>
    /// </summary>
    internal static readonly string[] CanonicalTypeCodes =
    [
        "physical_product",
        "service",
        "subscription",
        "package",
        "implementation",
        "support_sla",
        "addon",
        "license",
        "maintenance"
    ];

    private static readonly HashSet<string> CanonicalTypeCodeSet = new(CanonicalTypeCodes, StringComparer.Ordinal);

    /// <summary>
    /// Projects the effective document, or fails closed when the persisted state violates a
    /// structural invariant.
    ///
    /// <para>A missing override and an invalid override are deliberately not the same thing. A
    /// missing override is a valid sparse state that resolves to the ACTIVE default; an invalid
    /// override is corrupt owner-owned state and fails the whole read. Treating the invalid case as
    /// missing would silently turn a corrupt INACTIVE row into ACTIVE and re-enable a type an
    /// operator had deliberately disabled.</para>
    ///
    /// <para>Failure covers the whole document, never one entry: a partial document would misstate
    /// the Workspace's configuration, and its revision and ETag would describe a snapshot that never
    /// existed.</para>
    /// </summary>
    internal static ProductOperationResult<ConfigurationDocumentResponse> Project(ProductConfigurationState state)
    {
        if (state.Revision < 0)
            return ProductOperationResult<ConfigurationDocumentResponse>.Failure(ProductErrors.ConfigurationCorrupt());

        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in state.Overrides)
        {
            // Ordinal membership, so a case variant such as "Service" is not the canonical "service".
            // It is corrupt state and is never silently normalised into the canonical code.
            if (!CanonicalTypeCodeSet.Contains(item.ProductTypeCode))
                return ProductOperationResult<ConfigurationDocumentResponse>.Failure(ProductErrors.ConfigurationCorrupt());
            if (!string.Equals(item.Status, Active, StringComparison.Ordinal)
                && !string.Equals(item.Status, Inactive, StringComparison.Ordinal))
                return ProductOperationResult<ConfigurationDocumentResponse>.Failure(ProductErrors.ConfigurationCorrupt());
            // The composite primary key makes this unreachable through the database, so it is a
            // defence-in-depth check rather than the only guard.
            if (!overrides.TryAdd(item.ProductTypeCode, item.Status))
                return ProductOperationResult<ConfigurationDocumentResponse>.Failure(ProductErrors.ConfigurationCorrupt());
        }

        var types = new ProductConfigurationTypeEntry[CanonicalTypeCodes.Length];
        for (var index = 0; index < CanonicalTypeCodes.Length; index++)
        {
            var code = CanonicalTypeCodes[index];
            types[index] = new ProductConfigurationTypeEntry(
                code,
                overrides.TryGetValue(code, out var status) ? status : Active);
        }

        return ProductOperationResult<ConfigurationDocumentResponse>.Success(
            new ConfigurationDocumentResponse(state.Revision, new ProductConfigurationData(types)));
    }
}
