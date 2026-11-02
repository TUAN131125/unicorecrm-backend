using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal static class QuoteIndependentReviewRules
{
    internal static readonly string[] Criteria=["D1","D2","D3","D4","D5"];
    internal static bool Text(string? value,int max)=>!string.IsNullOrWhiteSpace(value)&&QuoteIdentityCaseService.Text(value,max);
    internal static bool Valid(SubmitQuoteIndependentReview r)=>r.Decision is "APPROVE_SIMULATION" or "REQUEST_MORE_EVIDENCE" or "REJECT_REVIEW"
        &&r.ReviewBindingFingerprint is {Length:64}&&r.ReviewBindingFingerprint.All(Uri.IsHexDigit)
        && Text(r.Reason,500)&&QuotePricing.EntityId(r.DocumentId)&&r.QuoteRevision>0&&r.PdfSha256 is {Length:64}&&r.PdfSha256.All(Uri.IsHexDigit)
        && QuotePricing.EntityId(r.SubjectId)&&r.IdentityVersion>=1&&r.Findings is {Length:<=5}
        && r.Findings.All(f=>f is not null&&Criteria.Contains(f.Criterion)&&f.Outcome is "PASS" or "FAIL" or "INSUFFICIENT"&&Text(f.Rationale,1000)
            &&f.ProofIds is {Length:<=5}&&f.ProofIds.All(QuotePricing.EntityId)&&f.ProofIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()==f.ProofIds.Length)
        && r.Findings.Select(f=>f.Criterion).Distinct().Count()==r.Findings.Length
        && (r.Consent is null||r.Consent.ProofIds is {Length:>=1 and <=3}&&r.Consent.ProofIds.All(QuotePricing.EntityId)
            &&QuoteIdentityEvaluation.TryTime(r.Consent.AcquiredAt,out _)&&Text(r.Consent.OriginReference,500)&&Text(r.Consent.StatementReference,1000)
            &&r.Consent.Finding is "EXPLICIT_UNCONDITIONAL_SIGNED_ASSENT" or "INSUFFICIENT" or "CONDITIONAL_OR_NEGATIVE");
    internal static bool Complete(SubmitQuoteIndependentReview r)=>Criteria.All(k=>r.Findings.Any(f=>f.Criterion==k&&f.Outcome=="PASS"&&f.ProofIds.Length>0))
        &&r.Consent is {Finding:"EXPLICIT_UNCONDITIONAL_SIGNED_ASSENT"}&&r.Findings.Single(f=>f.Criterion=="D1").ProofIds.Intersect(r.Consent.ProofIds,StringComparer.OrdinalIgnoreCase).Any();
    internal static bool Independent(string actor,string? creator,string? requester,string caseRecorder,IEnumerable<string> proofRecorders,
        string? identityRecorder,string? sourceCreator,string? sourcePreparer,string? checker)=>creator is not null&&identityRecorder is not null
        &&!new[]{creator,requester,caseRecorder,identityRecorder,sourceCreator,sourcePreparer,checker}.Concat(proofRecorders).Contains(actor,StringComparer.Ordinal);
    internal static string Status(string decision)=>decision switch{"APPROVE_SIMULATION"=>"REVIEW_APPROVED_SIMULATION","REJECT_REVIEW"=>"REVIEW_REJECTED",_=>"NEEDS_MORE_EVIDENCE"};
}
