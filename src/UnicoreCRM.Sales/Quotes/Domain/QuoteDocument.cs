namespace UnicoreCRM.Sales.Quotes.Domain;

internal sealed class QuoteDocument
{
    private QuoteDocument() { }
    internal QuoteDocument(string documentId, string workspaceId, string quoteId, string rootQuoteId, int quoteRevision, long sourceVersion,
        string bindingHash, string commercialFingerprint, string snapshotJson, long sellerVersion, string approvalPolicyVersion,
        string policyVersion, string templateVersion, int documentVersion, string issuedBy, DateTimeOffset issuedAt,
        string pdfSha256, int fileSize, int pageCount, string fieldManifestJson)
    {
        if (quoteRevision < 1 || sourceVersion < 1 || documentVersion < 1 || fileSize is < 1 or > 10485760 || pageCount is < 1 or > 100)
            throw new ArgumentException("Invalid issued document bounds.");
        DocumentId = documentId; WorkspaceId = workspaceId; QuoteId = quoteId;
        RootQuoteId = rootQuoteId; QuoteRevision = quoteRevision; SourceResourceVersion = sourceVersion;
        ResultResourceVersion = checked(sourceVersion + 1); BindingHash = bindingHash; CommercialFingerprint = commercialFingerprint;
        SnapshotJson = snapshotJson; SellerConfigurationVersion = sellerVersion; ApprovalPolicyVersion = approvalPolicyVersion;
        PolicyVersion = policyVersion; TemplateVersion = templateVersion; DocumentVersion = documentVersion;
        IssuedBy = issuedBy; IssuedAt = issuedAt; PdfSha256 = pdfSha256; FileSize = fileSize; PageCount = pageCount;
        FieldManifestJson = fieldManifestJson;
    }
    public string DocumentId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string QuoteId { get; private set; } = null!;
    public string RootQuoteId { get; private set; } = null!;
    public int QuoteRevision { get; private set; }
    public int DocumentVersion { get; private set; }
    public long SourceResourceVersion { get; private set; }
    public long ResultResourceVersion { get; private set; }
    public string BindingHash { get; private set; } = null!;
    public string CommercialFingerprint { get; private set; } = null!;
    public string SnapshotJson { get; private set; } = null!;
    public string FieldManifestJson { get; private set; } = null!;
    public long SellerConfigurationVersion { get; private set; }
    public string ApprovalPolicyVersion { get; private set; } = null!;
    public string PolicyVersion { get; private set; } = null!;
    public string TemplateVersion { get; private set; } = null!;
    public string IssuedBy { get; private set; } = null!;
    public DateTimeOffset IssuedAt { get; private set; }
    public string PdfSha256 { get; private set; } = null!;
    public int FileSize { get; private set; }
    public int PageCount { get; private set; }
}

internal sealed class QuoteDocumentContent
{
    private QuoteDocumentContent() { }
    internal QuoteDocumentContent(string workspaceId, string documentId, byte[] bytes)
    { WorkspaceId = workspaceId; DocumentId = documentId; Bytes = bytes.ToArray(); }
    public string WorkspaceId { get; private set; } = null!;
    public string DocumentId { get; private set; } = null!;
    public byte[] Bytes { get; private set; } = null!;
}
