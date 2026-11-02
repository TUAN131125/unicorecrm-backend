using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

// Audited human documentary workflow only. No operational-source adapter or acceptance authority.
internal sealed class QuoteManualSourceWorkflow(IQuoteIdentityPersistence store,IQuoteProofPersistence proofs,QuoteProofEffectiveScan scans,
    IQuoteIdentityPolicyReader policies,IQuotesPersistence quotes,IQuoteDocumentPersistence documents,IQuoteApprovalPolicyReader approvals,
    IWorkspaceTimeZoneReader zones,TimeProvider clock,IQuoteVerifiedAcceptancePolicyReader verification)
{
    internal const string Method="ORGANIZATION_ISSUED_AUTHORITY_WITH_INDEPENDENT_SOURCE_CHECK";
    internal const string Simulation="DEVELOPMENT_SIMULATION";
    internal bool Enabled(string w)=>policies.Read(w)is not null && policies.Simulation(w);
    internal static string SubjectFingerprint(QuoteAcceptanceCase c,QuoteIdentityBinding b)=>QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new{
        c.WorkspaceId,c.CaseId,c.QuoteId,c.RootQuoteId,c.QuoteRevision,c.DocumentId,c.IssuedPdfSha256,c.CommercialFingerprint,c.BuyerType,c.BuyerId,
        b.SubjectId,b.RecordedBy,b.Party.ContactId,b.Party.ContactVersion,b.Party.BuyerVersion,b.Party.RelationshipId,
        identity=b.IdentityProofs.OrderBy(x=>x.ProofId).ToArray(),authority=b.AuthorityProofs.OrderBy(x=>x.ProofId).ToArray(),scope=Scope(b.AuthorityClaim)},QuotePricing.Json));
    internal static string Scope(QuoteIdentityAuthorityClaim a)=>JsonSerializer.Serialize(new{from=a.EffectiveFrom,until=a.EffectiveUntil,currency=a.Currency,
        amount=decimal.TryParse(a.AmountLimit,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n)?n.ToString("G29",CultureInfo.InvariantCulture):a.AmountLimit,description=a.ScopeDescription?.Trim()},QuotePricing.Json);
    internal static string CheckFingerprint(QuoteAcceptanceCase c,QuoteIdentityBinding b,string policy)=>QuoteApprovalAssessment.Hash(SubjectFingerprint(c,b)+"|"+b.SourceId+"|"+b.SourceVersion+"|"+policy);
    internal static bool Fields(params string?[] values)=>values.All(x=>!string.IsNullOrWhiteSpace(x)&&QuoteIdentityCaseService.Text(x,1000));
    internal static bool References(string[]? ids)=>ids is {Length:>=1 and <=3}&&ids.All(QuotePricing.EntityId)&&ids.Distinct(StringComparer.OrdinalIgnoreCase).Count()==ids.Length;
    internal static bool Effective(string? from,string? until,DateTimeOffset now)=>QuoteIdentityEvaluation.TryTime(from,out var f)&&QuoteIdentityEvaluation.TryTime(until,out var u)&&f<u&&f<=now&&now<u;
    internal static bool Fresh(string? observed,DateTimeOffset now,int hours=24)=>hours is >=1 and <=24&&QuoteIdentityEvaluation.TryTime(observed,out var at)&&at<=now&&at>=now.AddHours(-hours);
    internal async Task<(QuoteAcceptanceCase? Case,QuoteIdentityCaseView? View)> CaseAsync(Quote q,string id,CancellationToken ct)
    {var c=await proofs.CaseAsync(q.WorkspaceId,q.QuoteId,id,false,ct);var r=c is null?null:await store.CurrentAsync(q.WorkspaceId,c.CaseId,ct);return(c,r is null?null:QuoteIdentityEvaluation.Decode<QuoteIdentityCaseView>(r.SnapshotJson));}
    internal async Task<QuoteOperationResult<QuoteSourceEvidence>> EvidenceAsync(Quote q,QuoteSourceEvidenceInput input,CancellationToken ct)
    {
        if(!Enabled(q.WorkspaceId))return EvidenceFail("BLOCKED_CONTRACT_ACTUAL_SOURCE_NOT_ADMITTED");
        if(q.BuyerType!="ORGANIZATION_ACCOUNT" || !string.Equals(input.IssuerOrganizationId,q.BuyerId,StringComparison.OrdinalIgnoreCase)|| !QuotePricing.EntityId(input.CaseId) || !References(input.IssuerProofIds)||!References(input.IndependentChannelProofIds)
            || input.ChannelOriginKind is not ("PUBLISHED_ORGANIZATION_DIRECTORY" or "PREEXISTING_INDEPENDENT_BUSINESS_RECORD" or "IN_PERSON_ORGANIZATION_OFFICE")
            || !Fields(input.ChannelOriginDescription,input.IndependentChannel) || !Effective(input.EffectiveFrom,input.EffectiveUntil,clock.GetUtcNow()))return EvidenceFail("SOURCE_EVIDENCE_OR_VALIDITY_INVALID");
        var(c,v)=await CaseAsync(q,input.CaseId,ct);if(c is null||v?.Binding is not {} binding)return EvidenceFail("SOURCE_CASE_BINDING_REQUIRED");
        var all=input.IssuerProofIds.Concat(input.IndependentChannelProofIds).ToArray();if(all.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=all.Length)return EvidenceFail("INDEPENDENT_CHANNEL_EVIDENCE_REQUIRED");
        var links=await LinksAsync(q.WorkspaceId,c.CaseId,all,ct);if(links is null)return EvidenceFail("PROOF_EFFECTIVE_CLEAN_REQUIRED");
        var issuer=links.Where(x=>input.IssuerProofIds.Contains(x.ProofId,StringComparer.OrdinalIgnoreCase)).ToArray();var channel=links.Except(issuer).ToArray();
        if(issuer.Any(x=>!binding.AuthorityProofs.Any(a=>a.ProofId==x.ProofId&&a.Sha256==x.Sha256))||channel.Any(x=>binding.IdentityProofs.Concat(binding.AuthorityProofs).Any(a=>a.Sha256==x.Sha256)))return EvidenceFail("INDEPENDENT_CHANNEL_EVIDENCE_REQUIRED");
        return QuoteOperationResult<QuoteSourceEvidence>.Success(new(q.QuoteId,c.CaseId,q.BuyerId,issuer,channel,input.ChannelOriginKind,input.ChannelOriginDescription.Trim(),input.IndependentChannel.Trim(),input.EffectiveFrom,input.EffectiveUntil,SubjectFingerprint(c,binding)));
    }
    internal async Task<bool> CurrentEvidenceAsync(Quote q,QuoteAcceptanceCase c,QuoteIdentityBinding b,QuoteSourceEvidence e,CancellationToken ct)
    {
        if(!string.Equals(e.IssuerOrganizationId,q.BuyerId,StringComparison.OrdinalIgnoreCase)||e.QuoteId!=q.QuoteId||e.CaseId!=c.CaseId||e.SubjectFingerprint!=SubjectFingerprint(c,b)||!Effective(e.EffectiveFrom,e.EffectiveUntil,clock.GetUtcNow()))return false;
        var links=e.IssuerProofs.Concat(e.IndependentChannelProofs).ToArray();var current=await LinksAsync(q.WorkspaceId,c.CaseId,links.Select(x=>x.ProofId).ToArray(),ct);
        return current is not null&&links.All(x=>current.Contains(x))&&e.IndependentChannelProofs.All(x=>!b.IdentityProofs.Concat(b.AuthorityProofs).Any(p=>p.Sha256==x.Sha256));
    }
    internal async Task<QuoteOperationResult<QuoteIdentitySourceCheck>> CheckAsync(Quote q,QuoteAcceptanceCase c,QuoteIdentityBinding b,QuoteEvidenceSource? source,
        QuoteManualSourceCheckInput input,string actor,string policy,CancellationToken ct)
    {
        if(!Enabled(q.WorkspaceId)||source is not {Status:"APPROVED",AssuranceLevel:Simulation,VerificationMethod:Method,Evidence:{},ApprovedBy:not null,EvidencePreparedBy:not null}||source.ResourceVersion!=b.SourceVersion||source.PolicyVersion!=policy||source.ApprovedBy==source.CreatedBy||source.ApprovedBy==source.EvidencePreparedBy)
            return CheckFail("APPROVED_CURRENT_SOURCE_REQUIRED");
        if(actor==source.ApprovedBy||actor==source.CreatedBy||actor==source.EvidencePreparedBy)return QuoteOperationResult<QuoteIdentitySourceCheck>.Failure(new("ACCESS_DENIED",403,"Independent source checker required","SELF_SOURCE_CHECK_FORBIDDEN"));
        if(!await CurrentEvidenceAsync(q,c,b,source.Evidence,ct))return CheckFail("SOURCE_EVIDENCE_BINDING_STALE");
        if(input.ConfirmedScope is null||input.ReportedOutcome is not ("CONFIRMED" or "NOT_CONFIRMED" or "UNAVAILABLE" or "INSUFFICIENT")||!Fresh(input.ObservedAt,clock.GetUtcNow(),verification.Read(q.WorkspaceId)?.AuthorityFreshnessHours??0)||!Fields(input.IndependentChannelUsed,input.ConfirmationSummary)
            ||input.IndependentChannelUsed.Trim()!=source.Evidence.IndependentChannel||Scope(input.ConfirmedScope)!=Scope(b.AuthorityClaim)||!References(input.ConfirmationProofIds))return CheckFail("SOURCE_CHECK_PROVENANCE_SCOPE_OR_FRESHNESS_INVALID");
        var links=await LinksAsync(q.WorkspaceId,c.CaseId,input.ConfirmationProofIds,ct);if(links is null)return CheckFail("PROOF_EFFECTIVE_CLEAN_REQUIRED");
        var other=b.IdentityProofs.Concat(b.AuthorityProofs).Concat(source.Evidence.IssuerProofs).Concat(source.Evidence.IndependentChannelProofs).ToArray();
        if(links.Any(l=>other.Any(o=>o.Sha256==l.Sha256)))return CheckFail("INDEPENDENT_CONFIRMATION_EVIDENCE_REQUIRED");
        foreach(var l in links)if(await store.ConfirmationAsync(q.WorkspaceId,c.CaseId,l.ProofId,l.Sha256,ct)is not null)return CheckFail("CONFIRMATION_ALREADY_RECORDED");
        var now=clock.GetUtcNow().UtcDateTime.ToString("O");var outcome=input.ReportedOutcome switch{"CONFIRMED"=>"SOURCE_CHECKED_MANUAL","NOT_CONFIRMED"=>"SOURCE_CHECK_FAILED","INSUFFICIENT"=>"INSUFFICIENT_EVIDENCE",_=>"SOURCE_UNAVAILABLE"};
        return QuoteOperationResult<QuoteIdentitySourceCheck>.Success(new(outcome,Method,now,actor,policy,links,[],source.SourceId,source.ResourceVersion,outcome=="SOURCE_CHECKED_MANUAL"?now:null,
            new(input.ObservedAt,links,input.IndependentChannelUsed.Trim(),input.ConfirmationSummary.Trim(),b.AuthorityClaim,CheckFingerprint(c,b,policy),Simulation)));
    }
    internal async Task<IReadOnlyList<string>> ReadinessAsync(Quote q,QuoteAcceptanceCase c,QuoteIdentityBinding b,QuoteEvidenceSource? source,QuoteIdentitySourceCheck? check,string policy,CancellationToken ct,bool verifyDocumentBytes=true)
    {
        var reasons=new List<string>();
        if(!Enabled(q.WorkspaceId))return ["BLOCKED_CONTRACT_IDENTITY_AUTHORITY_SOURCE"];
        if(source is not {Status:"APPROVED",AssuranceLevel:Simulation,VerificationMethod:Method,Evidence:{},ApprovedBy:not null,EvidencePreparedBy:not null}||source.ResourceVersion!=b.SourceVersion||source.PolicyVersion!=policy||source.ApprovedBy==source.CreatedBy||source.ApprovedBy==source.EvidencePreparedBy)return ["APPROVED_CURRENT_SOURCE_REQUIRED"];
        if(!await CurrentEvidenceAsync(q,c,b,source.Evidence,ct))reasons.Add("SOURCE_EVIDENCE_BINDING_STALE");
        var creator=await quotes.ReadCreatorAsync(q.WorkspaceId,q.QuoteId,ct);
        if(check is not {Outcome:"SOURCE_CHECKED_MANUAL",ManualCheck:{}}||check.SourceId!=source.SourceId||check.SourceVersion!=source.ResourceVersion||check.PolicyVersion!=policy)reasons.Add("SOURCE_CHECK_REQUIRED");
        else
        {
            var m=check.ManualCheck;
            if(m.AssuranceLevel!=Simulation||m.BindingFingerprint!=CheckFingerprint(c,b,policy)||Scope(m.ConfirmedScope)!=Scope(b.AuthorityClaim))reasons.Add("SOURCE_CHECK_BINDING_STALE");
            if(!Fresh(m.ObservedAt,clock.GetUtcNow(),verification.Read(q.WorkspaceId)?.AuthorityFreshnessHours??0))reasons.Add("SOURCE_CHECK_OBSERVATION_STALE");
            if(creator is null||check.ActorId==creator||check.ActorId==b.RecordedBy||check.ActorId==q.ApprovalRequestedBy||check.ActorId==source.CreatedBy||check.ActorId==source.ApprovedBy||check.ActorId==source.EvidencePreparedBy)reasons.Add("SOURCE_CHECK_DUTY_SEPARATION_INVALID");
            var current=await LinksAsync(q.WorkspaceId,c.CaseId,m.ConfirmationProofs.Select(x=>x.ProofId).ToArray(),ct);if(current is null||m.ConfirmationProofs.Any(x=>!current.Contains(x)))reasons.Add("PROOF_EFFECTIVE_CLEAN_REQUIRED");
        }
        reasons.AddRange(await CommercialReasonsAsync(q,c,ct,verifyDocumentBytes));return reasons;
    }
    internal async Task<IReadOnlyList<string>> CommercialReasonsAsync(Quote q,QuoteAcceptanceCase c,CancellationToken ct,bool verifyDocumentBytes=true)
    {
        var r=new List<string>();if(q.Status!="SENT"||q.ArchivedAt is not null)r.Add("QUOTE_STATE_INVALID");
        if(QuoteValidity.Evaluate(q.ValidUntil,await zones.ReadTimeZoneAsync(q.WorkspaceId,ct),clock.GetUtcNow()).State is "OVERDUE" or "TIMEZONE_UNAVAILABLE")r.Add("QUOTE_VALIDITY_OR_TIMEZONE_INVALID");
        _ = approvals; // Historical constructor compatibility; no B1 prerequisite.
        if(!QuoteVerifiedAcceptanceRules.HasCurrentManualDelivery(q.DeliveryHistoryJson,q.QuoteRevision,QuoteApprovalAssessment.ContentFingerprint(q)))r.Add("COMMERCIAL_DELIVERY_EVIDENCE_UNAVAILABLE");
        var d=await documents.FindAsync(q.WorkspaceId,q.QuoteId,c.DocumentId,ct);var bytes=!verifyDocumentBytes||d is null?null:await documents.ReadBytesAsync(q.WorkspaceId,d.DocumentId,ct);
        if(d is null||d.PdfSha256!=c.IssuedPdfSha256||d.CommercialFingerprint!=c.CommercialFingerprint||d.RootQuoteId!=c.RootQuoteId||d.QuoteRevision!=c.QuoteRevision||verifyDocumentBytes&&(bytes is null||bytes.Length!=d.FileSize||Convert.ToHexString(SHA256.HashData(bytes))!=d.PdfSha256))r.Add("ISSUED_DOCUMENT_INTEGRITY_OR_BINDING_INVALID");return r;
    }
    private async Task<QuoteIdentityProofLink[]?> LinksAsync(string w,string c,IReadOnlyList<string> ids,CancellationToken ct)
    {
        var rows=await store.ProofsAsync(w,c,ids,ct);if(rows.Count!=ids.Distinct(StringComparer.OrdinalIgnoreCase).Count())return null;var clean=await scans.ReadAsync(w,rows,false,ct);
        return rows.All(x=>clean[x.ProofId].Reviewable)?rows.Select(x=>new QuoteIdentityProofLink(x.ProofId,x.Sha256,x.EvidenceType)).ToArray():null;
    }
    private static QuoteOperationResult<QuoteSourceEvidence> EvidenceFail(string reason)=>QuoteOperationResult<QuoteSourceEvidence>.Failure(QuoteIdentityAccess.Block(reason));
    private static QuoteOperationResult<QuoteIdentitySourceCheck> CheckFail(string reason)=>QuoteOperationResult<QuoteIdentitySourceCheck>.Failure(QuoteIdentityAccess.Block(reason));
}
