namespace UnicoreCRM.Sales.Products.Domain;

/// <summary>
/// The Products-owned revision anchor of one Workspace's Product Configuration document.
///
/// <para>The revision belongs to the logical configuration document, not to the override rows, so it
/// must survive a Workspace whose overrides have all been deleted. A Workspace at revision 5 whose
/// last override is removed keeps revision 5 and reports an all-ACTIVE effective document; deriving
/// the revision from the override rows would silently reset it to 0 and invalidate every ETag
/// already served.</para>
///
/// <para>Absence of this record is a valid sparse state meaning revision 0. It is never created by a
/// read.</para>
/// </summary>
internal sealed class ProductConfigurationDocumentRecord
{
    private ProductConfigurationDocumentRecord() { }

    internal ProductConfigurationDocumentRecord(string workspaceId, long revision)
    {
        WorkspaceId = workspaceId;
        Revision = revision;
    }

    public string WorkspaceId { get; private set; } = null!;
    public long Revision { get; private set; }
}

/// <summary>
/// One Workspace override of the eligibility status of a canonical ProductType code.
///
/// <para>Persistence is sparse: a canonical code with no row takes the default effective status. The
/// row therefore records a deviation, never the vocabulary itself, and <see cref="ProductTypeCode"/>
/// is a reference to the canonical contract vocabulary rather than authority to redefine it.</para>
///
/// <para>Identity is the canonical code itself - the primary key is
/// (<see cref="WorkspaceId"/>, <see cref="ProductTypeCode"/>). No opaque overlay identifier exists,
/// so the key survives a delete and re-create of the same override.</para>
/// </summary>
internal sealed class ProductConfigurationTypeOverride
{
    private ProductConfigurationTypeOverride() { }

    internal ProductConfigurationTypeOverride(string workspaceId, string productTypeCode, string status)
    {
        WorkspaceId = workspaceId;
        ProductTypeCode = productTypeCode;
        Status = status;
    }

    public string WorkspaceId { get; private set; } = null!;
    public string ProductTypeCode { get; private set; } = null!;
    public string Status { get; private set; } = null!;
}
