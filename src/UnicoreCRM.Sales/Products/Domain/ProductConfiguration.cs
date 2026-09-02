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

    /// <summary>
    /// Advances the document revision by exactly one. It is the only way the anchor moves, so a
    /// mutation cannot skip, reuse or reverse a revision, and the concurrency token on
    /// <see cref="Revision"/> turns a lost update into a version conflict rather than a silent
    /// overwrite.
    /// </summary>
    internal void Advance() => Revision++;
}

/// <summary>
/// The greatest Product Configuration revision this Workspace has ever successfully served.
///
/// <para>It exists because a revision cannot attest to its own history. If the current anchor were
/// both the value under validation and the only record of what came before, a rollback from 5 to 3
/// would be indistinguishable from a Workspace that had only ever reached 3 - the corrupt value
/// would be validating itself. This record is deliberately separate durable evidence, written only
/// on a successful read and never derived from the anchor.</para>
///
/// <para>It is monotonic: it is only ever raised. Absence means nothing has been trusted yet, which
/// is equivalent to 0, so a Workspace serving revision 0 needs no row - the record is integrity
/// evidence, never Product Configuration materialisation.</para>
/// </summary>
internal sealed class ProductConfigurationTrustedRevision
{
    private ProductConfigurationTrustedRevision() { }

    public string WorkspaceId { get; private set; } = null!;
    public long GreatestTrustedRevision { get; private set; }
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

    /// <summary>
    /// Retargets an existing override. The canonical code is identity and is never rewritten, so a
    /// status change reuses the row the key already names instead of deleting and recreating it.
    /// </summary>
    internal void SetStatus(string status) => Status = status;
}
