namespace UnicoreCRM.Sales.Quotes.Domain;

internal sealed class QuoteAcceptanceCase
{
    private QuoteAcceptanceCase() { }
    internal QuoteAcceptanceCase(string workspaceId, string caseId, string quoteId, string rootQuoteId, int quoteRevision,
        string documentId, string commercialFingerprint, string issuedPdfSha256, string buyerType, string buyerId,
        string policyVersion, string createdBy, DateTimeOffset createdAt)
    {
        if (quoteRevision < 1 || commercialFingerprint.Length != 64 || issuedPdfSha256.Length != 64) throw new ArgumentException("Invalid case binding.");
        WorkspaceId=workspaceId; CaseId=caseId; QuoteId=quoteId; RootQuoteId=rootQuoteId; QuoteRevision=quoteRevision;
        DocumentId=documentId; CommercialFingerprint=commercialFingerprint; IssuedPdfSha256=issuedPdfSha256;
        BuyerType=buyerType; BuyerId=buyerId; PolicyVersion=policyVersion; CreatedBy=createdBy; CreatedAt=createdAt; UpdatedAt=createdAt;
    }
    public string WorkspaceId { get; private set; } = null!;
    public string CaseId { get; private set; } = null!;
    public string QuoteId { get; private set; } = null!;
    public string RootQuoteId { get; private set; } = null!;
    public int QuoteRevision { get; private set; }
    public string DocumentId { get; private set; } = null!;
    public string CommercialFingerprint { get; private set; } = null!;
    public string IssuedPdfSha256 { get; private set; } = null!;
    public string BuyerType { get; private set; } = null!;
    public string BuyerId { get; private set; } = null!;
    public string PolicyVersion { get; private set; } = null!;
    public string CaseStatus { get; private set; } = "COLLECTING_PROOFS";
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long ResourceVersion { get; private set; } = 1;
    internal void RecordQuarantine(DateTimeOffset now) { CaseStatus="QUARANTINED"; UpdatedAt=now; ResourceVersion=checked(ResourceVersion+1); }
}

internal sealed class QuoteAcceptanceProof
{
    private QuoteAcceptanceProof() { }
    internal QuoteAcceptanceProof(string workspaceId, string caseId, string proofId, string evidenceType, string fileName,
        int fileSize, string sha256, string policyVersion, string uploadedBy, DateTimeOffset uploadedAt)
    {
        if (fileSize is < 1 or > 10485760 || sha256.Length != 64) throw new ArgumentException("Invalid proof bounds.");
        WorkspaceId=workspaceId; CaseId=caseId; ProofId=proofId; EvidenceType=evidenceType; FileName=fileName;
        FileSize=fileSize; Sha256=sha256; PolicyVersion=policyVersion; UploadedBy=uploadedBy; UploadedAt=uploadedAt;
    }
    public string WorkspaceId { get; private set; } = null!;
    public string CaseId { get; private set; } = null!;
    public string ProofId { get; private set; } = null!;
    public string EvidenceType { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public string MediaType { get; private set; } = "application/pdf";
    public int FileSize { get; private set; }
    public string Sha256 { get; private set; } = null!;
    public string PolicyVersion { get; private set; } = null!;
    public string StorageStatus { get; private set; } = "QUARANTINED";
    public string ScanStatus { get; private set; } = "SCAN_PENDING";
    public string UploadedBy { get; private set; } = null!;
    public DateTimeOffset UploadedAt { get; private set; }
    public string Retention { get; private set; } = "INDEFINITE_TECHNICAL_B61";
    public long ResourceVersion { get; private set; } = 1;
}

internal sealed class QuoteAcceptanceProofContent
{
    private QuoteAcceptanceProofContent() { }
    internal QuoteAcceptanceProofContent(string workspaceId, string caseId, string proofId, byte[] protectedBytes, string sha256, int length)
    { WorkspaceId=workspaceId; CaseId=caseId; ProofId=proofId; ProtectedBytes=protectedBytes.ToArray(); Sha256=sha256; ContentLength=length; }
    public string WorkspaceId { get; private set; } = null!;
    public string CaseId { get; private set; } = null!;
    public string ProofId { get; private set; } = null!;
    public byte[] ProtectedBytes { get; private set; } = null!;
    public string Sha256 { get; private set; } = null!;
    public int ContentLength { get; private set; }
    public string ProtectionFormat { get; private set; } = "ASPNET_DP_QUOTE_PROOF_V1";
}
