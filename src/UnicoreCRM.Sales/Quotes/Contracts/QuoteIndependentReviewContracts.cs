using System.Text.Json.Serialization;
namespace UnicoreCRM.Sales.Quotes.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteReviewFinding(string Criterion,string Outcome,string Rationale,string[] ProofIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteConsentReview(string[] ProofIds,string AcquiredAt,string OriginReference,string StatementReference,string Finding);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SubmitQuoteIndependentReview(string Decision,string Reason,string DocumentId,int QuoteRevision,string PdfSha256,
    string SubjectId,long IdentityVersion,QuoteReviewFinding[] Findings,QuoteConsentReview? Consent=null,string? ReviewBindingFingerprint=null);
public sealed record QuoteReviewProof(string ProofId,string Sha256,string EvidenceType,string ScanPolicyVersion,string ScannedAt);
public sealed record QuoteIndependentReviewDecision(string ReviewDecisionId,string WorkspaceId,string CaseId,string QuoteId,string RootQuoteId,
    string DocumentId,int QuoteRevision,string PdfSha256,string CommercialFingerprint,string IdentitySubjectId,long IdentityVersion,
    string IdentityFingerprint,long AuthorityBindingVersion,string SourceCheckId,long SourceCheckVersion,string? SourceId,long? SourceVersion,string SourceCheckFingerprint,string SourceProvenanceDigest,
    IReadOnlyList<QuoteReviewProof> Proofs,IReadOnlyList<QuoteReviewFinding> Findings,QuoteConsentReview? Consent,string Decision,string Reason,
    string ReviewerId,string ReviewedAt,string ReviewPolicyVersion,string Environment,string AssuranceLevel="DEVELOPMENT_SIMULATION",bool ActualSourceVerification=false,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? ReviewBindingFingerprint=null);
public sealed record QuoteReviewDocument(string DocumentId,int QuoteRevision,string PdfSha256,string CommercialFingerprint);
public sealed record QuoteIndependentReviewView(string CaseId,string QuoteId,long ResourceVersion,string Status,bool ReadyForIndependentReview,
    IReadOnlyList<string> Blockers,string ReviewPolicyVersion,QuoteIdentityCaseView Identity,QuoteReviewDocument Document,string ReviewBindingFingerprint,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]QuoteIndependentReviewDecision? LastDecision,
    string AssuranceLevel="UNVERIFIED",bool ActualSourceVerification=false);
