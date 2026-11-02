using System.Text.Json.Serialization;
namespace UnicoreCRM.Sales.Quotes.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteIdentityAuthorityClaim(string? EffectiveFrom=null,string? EffectiveUntil=null,string? Currency=null,string? AmountLimit=null,string? ScopeDescription=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveQuoteIdentityBindingRequest(string ContactId,string[] IdentityProofIds,string[] AuthorityProofIds,
    string? RelationshipId=null,string? ExpectedSubjectId=null,string? SourceId=null,long? SourceVersion=null,QuoteIdentityAuthorityClaim? AuthorityClaim=null,string? Reason=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteIdentityActionRequest(string SubjectId,string? Reason=null,QuoteManualSourceCheckInput? ManualCheck=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteEvidenceSourceRequest(string Name,string SourceType,string IssuerReference,string VerificationMethod,string ProvenanceReference,string Requirements,string? Reason=null,QuoteSourceEvidenceInput? Evidence=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteEvidenceSourceAction(string Reason);

public sealed record QuoteIdentityProofLink(string ProofId,string Sha256,string EvidenceType);
public sealed record QuoteIdentityBinding(string SubjectId,QuoteAcceptancePartySnapshot Party,IReadOnlyList<QuoteIdentityProofLink> IdentityProofs,
    IReadOnlyList<QuoteIdentityProofLink> AuthorityProofs,QuoteIdentityAuthorityClaim AuthorityClaim,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? SourceId,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]long? SourceVersion,string RecordedBy,string RecordedAt,string TrustStatus="DOCUMENT_SUBMITTED");
public sealed record QuoteIdentityAssessment(string Status,string IdentityStatus,string AuthorityStatus,string SourceStatus,IReadOnlyList<string> Reasons,
    string AssessedAt,string PolicyVersion,bool ReadyForIndependentReview=false,string AssuranceLevel="UNVERIFIED",bool ActualSourceVerification=false);
public sealed record QuoteIdentitySourceCheck(string Outcome,string VerificationMethod,string AttemptedAt,string ActorId,string PolicyVersion,
    IReadOnlyList<QuoteIdentityProofLink> EvidenceReferences,IReadOnlyList<string> Reasons,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? SourceId=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]long? SourceVersion=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? CheckedAt=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]QuoteManualSourceCheckEvidence? ManualCheck=null);
public sealed record QuoteIdentityCaseView(string CaseId,string QuoteId,long ResourceVersion,QuoteBuyerReference BuyerRef,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]QuoteIdentityBinding? Binding,
    QuoteIdentityAssessment Assessment,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]QuoteIdentitySourceCheck? LastSourceCheck=null);
public sealed record QuoteEvidenceSource(string SourceId,long ResourceVersion,QuoteBuyerReference BuyerRef,string Name,string SourceType,string IssuerReference,
    string VerificationMethod,string ProvenanceReference,string Requirements,string Status,string CreatedBy,string RecordedBy,string RecordedAt,string PolicyVersion,
    string ChangeReason,bool CanApprove=false,string AdmissionBlocker="BLOCKED_CONTRACT_SOURCE_METHOD_NOT_ADMITTED",
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]QuoteSourceEvidence? Evidence=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? ApprovedBy=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? ApprovedAt=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? AssuranceLevel=null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? EvidencePreparedBy=null);
public sealed record QuoteIdentityHistoryItem(long ResourceVersion,string Kind,string ActorId,string RecordedAt,string Reason,QuoteIdentityAssessment Assessment,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]string? SubjectId,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]QuoteIdentitySourceCheck? SourceCheck);
public sealed record QuoteIdentityMutation<T>(string CommandId,string CorrelationId,string AggregateId,string AggregateType,long Version,string OccurredAt,
    string Outcome,T Result,IReadOnlyList<string> EventIds,IReadOnlyList<string> AuditIds);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteSourceEvidenceInput(string CaseId,string IssuerOrganizationId,string[] IssuerProofIds,string[] IndependentChannelProofIds,
    string ChannelOriginKind,string ChannelOriginDescription,string IndependentChannel,string EffectiveFrom,string EffectiveUntil);
public sealed record QuoteSourceEvidence(string QuoteId,string CaseId,string IssuerOrganizationId,IReadOnlyList<QuoteIdentityProofLink> IssuerProofs,IReadOnlyList<QuoteIdentityProofLink> IndependentChannelProofs,
    string ChannelOriginKind,string ChannelOriginDescription,string IndependentChannel,string EffectiveFrom,string EffectiveUntil,string SubjectFingerprint);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteManualSourceCheckInput(string ReportedOutcome,string ObservedAt,string[] ConfirmationProofIds,
    string IndependentChannelUsed,string ConfirmationSummary,QuoteIdentityAuthorityClaim ConfirmedScope);
public sealed record QuoteManualSourceCheckEvidence(string ObservedAt,IReadOnlyList<QuoteIdentityProofLink> ConfirmationProofs,
    string IndependentChannelUsed,string ConfirmationSummary,QuoteIdentityAuthorityClaim ConfirmedScope,string BindingFingerprint,string AssuranceLevel);
