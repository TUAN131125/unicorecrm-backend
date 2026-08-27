using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Sales.Products.Application.ProvideProductRecordAccessFacts;

/// <summary>
/// The Products side of the narrow record-access fact boundary.
///
/// <para>Product carries no member-owner concept, so the facts it reports contain no owner reference
/// and OWN scope consequently denies every Product record. That is a recorded AUTHORITY_GAP: nothing
/// - creator, last editor, category or supplier - is substituted for a record owner, because no
/// authority proves any of them equivalent to one.</para>
/// </summary>
internal sealed class ProductRecordAccessFactProvider(IProductsPersistence persistence) : IRecordAccessFactProvider
{
    /// <summary>
    /// Only capabilities behind an admitted Products operation are declared. Products has no export
    /// or approval operation, so neither is declared. The edit capability is <c>products.edit</c>,
    /// not <c>products.update</c>, so the owner's own spelling is used rather than a guessed one.
    /// </summary>
    private static readonly RecordAccessResourceDescriptor ProductsDescriptor = RecordAccessResourceDescriptor.Create(
        resourceKey: ProductAuthorization.ResourceKey,
        readCapability: ProductCapabilities.Read.Capability,
        updateCapability: ProductCapabilities.Edit.Capability,
        deleteCapability: ProductCapabilities.Delete.Capability,
        commandCapabilities: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["product.create"] = ProductCapabilities.Create.Capability,
            ["product.update"] = ProductCapabilities.Edit.Capability,
            ["product.archive"] = ProductCapabilities.Delete.Capability
        },
        enforceableFields: ProductFieldSecurity.EnforceableFields);

    public RecordAccessResourceDescriptor Descriptor => ProductsDescriptor;

    public async Task<RecordAccessFacts> ReadFactsAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string recordId,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        if (!ProductValidation.IsEntityId(recordId))
            return RecordAccessFacts.NotFound;

        // The lookup is already constrained to the trusted Workspace, so a Product of another
        // Workspace is reported as not found rather than being read and then rejected.
        var product = await persistence.ReadProductAsync(trustedWorkspace.WorkspaceId, recordId, cancellationToken);
        return product is null ? RecordAccessFacts.NotFound : ProductAuthorization.Facts(product);
    }
}
