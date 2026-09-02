using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.Common;

/// <summary>The persisted Product Configuration state of one Workspace, read as one snapshot.</summary>
/// <param name="TrustedRevision">
/// The greatest revision this Workspace has ever successfully served, read in the same snapshot as
/// <paramref name="Revision"/> so the monotonicity comparison cannot straddle two states.
/// </param>
internal sealed record ProductConfigurationState(
    long Revision,
    long TrustedRevision,
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
    /// Whether a supplied path identifier is one of the nine canonical codes. Membership is ordinal,
    /// so a case variant such as "Service" is not the canonical "service" and identifies no resource
    /// at all. The vocabulary is contract-global and identical in every Workspace, so answering it
    /// discloses no Workspace state and cannot reveal whether an override row exists.
    /// </summary>
    internal static bool IsCanonicalTypeCode(string code) => CanonicalTypeCodeSet.Contains(code);

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

        // A revision below one already served is a rollback, and 3 after 5 is structurally
        // indistinguishable from a Workspace that only ever reached 3 without this separate evidence.
        // Serving it would silently reuse ETag "3" for a document that no longer matches what that
        // validator once described. A revision above the trusted mark is not corrupt: a committed
        // mutation may legitimately have advanced the document without this node ever serving it.
        if (state.Revision < state.TrustedRevision)
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

/// <summary>
/// Decides whether a canonical ProductType may be newly selected by a Product command.
///
/// <para>Deliberately separate from <see cref="ProductValidation"/>: that helper answers the
/// contract-global question "is this a canonical ProductType?" and stays a pure static with no
/// Workspace or persistence dependency. This one answers the Workspace-scoped question "may this
/// canonical type be newly selected here?", and runs only after the canonical answer is yes.</para>
///
/// <para>It derives the effective state through <see cref="ProductConfigurationCatalog.Project"/>,
/// the same projection the public read uses, so command and read semantics cannot drift apart.</para>
/// </summary>
internal static class ProductTypeEligibility
{
    /// <param name="existingType">
    /// The type the Product already carries, or null for a creation. Preserving a type a Product
    /// already has is not a new selection, so an INACTIVE status does not block it - otherwise a
    /// Workspace that retires a type would freeze every Product still using it.
    /// </param>
    /// <returns>Null when the selection is permitted, otherwise the error to fail with.</returns>
    internal static ProductOperationError? Evaluate(
        ProductConfigurationState state,
        string requestedType,
        string? existingType)
    {
        var projected = ProductConfigurationCatalog.Project(state);
        if (!projected.IsSuccess)
        {
            // Corrupt configuration leaves the effective state undefined. It is not "the type is
            // INACTIVE", and it is emphatically not "the type is ACTIVE": the command fails closed
            // with the system error rather than guessing either way, and rather than reporting a
            // server integrity fault as though the caller had sent a bad field.
            return projected.Error;
        }

        // Preserving the exact existing type is never a new selection, whatever its status.
        if (existingType is not null && string.Equals(requestedType, existingType, StringComparison.Ordinal))
            return null;

        var entry = projected.Value!.Data.Types.SingleOrDefault(
            item => string.Equals(item.Code, requestedType, StringComparison.Ordinal));
        if (entry is null)
        {
            // Unreachable for a canonically validated request, because the projection always emits
            // every canonical code. Reaching it would mean the two vocabularies had diverged, which
            // is an integrity fault rather than a caller error.
            return ProductErrors.ConfigurationCorrupt();
        }

        return string.Equals(entry.Status, ProductConfigurationCatalog.Active, StringComparison.Ordinal)
            ? null
            : ProductErrors.FieldValidation(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["type"] = ["type is not currently selectable in this Workspace."]
            });
    }
}
