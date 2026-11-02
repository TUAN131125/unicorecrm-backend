using System.Text.Json.Serialization;
namespace UnicoreCRM.Sales.Quotes.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateQuoteAcceptanceCaseRequest(string DocumentId, int QuoteRevision, string PdfSha256, string CommercialContentFingerprint);
public sealed record QuoteAcceptanceCaseMetadata(string CaseId, string QuoteId, string RootQuoteId, int QuoteRevision, string DocumentId,
    string CommercialContentFingerprint, string PdfSha256, QuoteBuyerReference BuyerRef, string CaseStatus, string CreatedAt, string CreatedBy, long ResourceVersion);
public sealed record QuoteAcceptanceProofMetadata(string ProofId, string CaseId, string QuoteId, string EvidenceType, string FileName,
    string MediaType, int FileSize, string Sha256, string StorageStatus, string ScanStatus, string UploadedAt, string UploadedBy,
    long ResourceVersion, string Retention, bool ContentAvailable = false,string StructuralStatus="NOT_COMPLETED",string MalwareVerdict="NOT_COMPLETED",
    string ScanReasonCode="SCAN_PENDING",[property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? ScannedAt=null,
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? ScanPolicyVersion=null,bool Reviewable=false);
public sealed record QuoteProofMutationResult(QuoteAcceptanceCaseMetadata Case, [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] QuoteAcceptanceProofMetadata? Proof = null);
public sealed record QuoteProofMutation(string CommandId, string CorrelationId, string AggregateId, string AggregateType, long Version,
    string OccurredAt, string Outcome, QuoteProofMutationResult Result, IReadOnlyList<string> EventIds, IReadOnlyList<string> AuditIds);
public sealed record QuoteProofPage<T>(IReadOnlyList<T> Items, QuoteProofPageInfo PageInfo);
public sealed record QuoteProofPageInfo(bool HasNextPage, [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? NextCursor);
public sealed record QuoteProofSecurity(string ProofId, string StorageStatus, string ScanStatus, bool ContentAvailable=false, bool ScannerAvailable=false,
    string StructuralStatus="NOT_COMPLETED",string MalwareVerdict="NOT_COMPLETED",string ReasonCode="SCAN_PENDING",
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? ScannedAt=null,[property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? ScanPolicyVersion=null,bool Reviewable=false);
public sealed record QuoteProofScanHistoryItem(string AttemptId,int AttemptNumber,string Status,string ReasonCode,string StructuralStatus,string MalwareVerdict,
    string StartedAt,[property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? CompletedAt,string PolicyVersion,string InputSha256,
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? EngineVersion,[property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? SignatureVersion,
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? SignatureUpdatedAt);
