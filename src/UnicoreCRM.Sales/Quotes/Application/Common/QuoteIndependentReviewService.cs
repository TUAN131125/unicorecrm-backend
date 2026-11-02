using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteIndependentReviewService(QuoteIdentityAccess access,QuoteIdentityEvaluation evaluation,IQuoteIdentityPersistence store,
    IQuoteProofPersistence proofs,QuoteProofEffectiveScan scans,IQuoteIdentityPolicyReader policies,IQuotesPersistence quotes,
    IQuoteAcceptancePartyReader parties,IQuoteIdentityCommitProbe probe,TimeProvider clock)
{
    private sealed record Facts(QuoteIdentityCaseView Identity,QuoteEvidenceSource? Source,IReadOnlyList<QuoteAcceptanceProof> Proofs,
        IReadOnlyDictionary<string,QuoteProofSecurity> Security,string? Creator,string? Policy,long BindingVersion,long CheckVersion);
    internal async Task<QuoteOperationResult<QuoteIndependentReviewView>> ReadAsync(string q,string c,QuoteRequestMetadata meta,CancellationToken ct)
    {
        var a=await access.AuthorizeAsync(q,c,meta,ct);if(!a.IsSuccess)return FailView(a.Error!);
        var f=await FactsAsync(a.Value!,meta,ct);if(!f.IsSuccess)return FailView(f.Error!);
        var latest=await store.ReviewsAsync(a.Value!.Grant.Quote.WorkspaceId,c,0,1,ct);var decision=latest.Count==0?null:Decode(latest[0]);
        var view=View(a.Value,f.Value!,decision);await AuditAsync(a.Value,meta,"getQuoteIndependentReview",ct);
        return QuoteOperationResult<QuoteIndependentReviewView>.Success(view);
    }
    internal async Task<QuoteOperationResult<QuoteProofPage<QuoteIndependentReviewDecision>>> HistoryAsync(string q,string c,int offset,int limit,QuoteRequestMetadata meta,CancellationToken ct)
    {
        var a=await access.AuthorizeAsync(q,c,meta,ct);if(!a.IsSuccess)return QuoteOperationResult<QuoteProofPage<QuoteIndependentReviewDecision>>.Failure(a.Error!);
        if(offset is <0 or >10000||limit is <1 or >50)return QuoteOperationResult<QuoteProofPage<QuoteIndependentReviewDecision>>.Failure(Input());
        var current=await evaluation.ViewAsync(a.Value!.Grant.Quote,a.Value.Case!,meta,ct,verifyDocumentBytes:false);
        if(!current.IsSuccess)return QuoteOperationResult<QuoteProofPage<QuoteIndependentReviewDecision>>.Failure(current.Error!);
        var rows=await store.ReviewsAsync(a.Value.Grant.Quote.WorkspaceId,c,offset,limit+1,ct);
        await AuditAsync(a.Value,meta,"listQuoteIndependentReviewHistory",ct);
        return QuoteOperationResult<QuoteProofPage<QuoteIndependentReviewDecision>>.Success(new(rows.Take(limit).Select(Decode).ToArray(),new(rows.Count>limit,rows.Count>limit?(offset+limit).ToString(CultureInfo.InvariantCulture):null)));
    }
    internal async Task<QuoteOperationResult<QuoteIdentityMutation<QuoteIndependentReviewView>>> SubmitAsync(string q,string c,SubmitQuoteIndependentReview request,QuoteCommandMetadata command,CancellationToken ct)
    {
        var meta=new QuoteRequestMetadata(command.RequestId,command.CorrelationId);
        var a=await access.AuthorizeAsync(q,c,meta,ct,QuoteCapabilities.VerifyAcceptanceEvidence,content:true);if(!a.IsSuccess)return Fail(a.Error!);
        if(!QuoteIndependentReviewRules.Valid(request))return Fail(Input());
        var context=a.Value!;q=context.Grant.Quote.QuoteId;c=context.Case!.CaseId;var w=context.Grant.Quote.WorkspaceId;var actor=context.Grant.Grants[0].Trusted.MemberId;
        const string op="submitQuoteIndependentReviewDecision";
        var scope=QuoteIdentityCaseService.Scope(w,actor,op,c,command.IdempotencyKey);var fingerprint=QuoteIdentityCaseService.Fingerprint(request,command.ExpectedVersion);
        await using var tx=await quotes.BeginAsync(ct);await quotes.LockAsync(scope,ct);await quotes.LockAsync($"aggregate:{w}:{context.Grant.Quote.QuoteId}",ct);await quotes.LockAsync("identity-case:"+w.ToLowerInvariant()+":"+c.ToLowerInvariant(),ct);
        a=await access.AuthorizeAsync(q,c,meta,ct,QuoteCapabilities.VerifyAcceptanceEvidence,content:true);if(!a.IsSuccess)return Fail(a.Error!);context=a.Value!;
        var row=await store.CurrentAsync(w,c,ct);if(row is null)return Fail(Block("IDENTITY_BINDING_REQUIRED"));
        var identity=QuoteIdentityEvaluation.Decode<QuoteIdentityCaseView>(row.SnapshotJson);
        if(identity.Binding is not {} binding)return Fail(Block("IDENTITY_BINDING_REQUIRED"));
        if(binding.SourceId is not null)await quotes.LockAsync("identity-source:"+w.ToLowerInvariant()+":"+binding.SourceId.ToLowerInvariant(),ct);
        await using var held=await parties.AcquireAsync(context.Case!.BuyerType,context.Case.BuyerId,binding.Party.ContactId,binding.Party.RelationshipId,meta.RequestId,meta.CorrelationId,ct);
        if(held.Snapshot is not {} party||party.WorkspaceId!=w)return Fail(QuoteErrors.AccessDenied());
        var f=await FactsAsync(context,meta,ct,party,true);if(!f.IsSuccess)return Fail(f.Error!);var facts=f.Value!;
        if(!Independent(context,facts,actor))return Fail(new("ACCESS_DENIED",403,"Independent reviewer required","SELF_REVIEW_FORBIDDEN"));
        var saved=await quotes.FindCommandAsync(scope,ct);
        if(saved is not null)
        {
            if(saved.Fingerprint!=fingerprint)return Fail(new("IDEMPOTENCY_KEY_REUSED",409,"Idempotency key reused"));
            var receipt=QuoteIdentityEvaluation.Decode<QuoteIdentityMutation<QuoteIndependentReviewView>>(saved.ResponseJson);
            // Reauthorize the historical subject, then project CURRENT review facts instead of replaying stale approval.
            if(receipt.Result.Identity.Binding?.Party.ContactId!=party.ContactId||receipt.Result.Identity.Binding?.Party.RelationshipId!=party.RelationshipId)return Fail(QuoteErrors.AccessDenied());
            var latest=await store.ReviewsAsync(w,c,0,1,ct);
            return QuoteOperationResult<QuoteIdentityMutation<QuoteIndependentReviewView>>.Success(receipt with{Outcome="REPLAYED",Result=View(context,facts,latest.Count==0?null:Decode(latest[0]))});
        }
        if(command.ExpectedVersion!=row.ResourceVersion||request.IdentityVersion!=row.ResourceVersion)return Fail(QuoteIdentityAccess.Stale());
        if(request.ReviewBindingFingerprint!=ReviewFingerprint(context,facts))return Fail(Block("REVIEW_FACTS_CHANGED"));
        var caseRow=context.Case;
        if(request.SubjectId!=binding.SubjectId||request.DocumentId!=caseRow.DocumentId||request.QuoteRevision!=caseRow.QuoteRevision||request.PdfSha256!=caseRow.IssuedPdfSha256)return Fail(Block("REVIEW_DOCUMENT_OR_SUBJECT_MISMATCH"));
        if(facts.Policy is null)return Fail(Block("INDEPENDENT_REVIEW_POLICY_UNAVAILABLE"));
        var referenced=request.Findings.SelectMany(x=>x.ProofIds).Concat(request.Consent?.ProofIds??[]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(referenced.Any(id=>!facts.Proofs.Any(p=>p.ProofId==id)))return Fail(Block("REVIEW_PROOF_REFERENCE_INVALID"));
        if(request.Decision=="APPROVE_SIMULATION")
        {
            if(!QuoteIndependentReviewRules.Complete(request))return Fail(Block("D1_D5_FINDINGS_INCOMPLETE"));
            var blockers=PositiveBlockers(facts).ToList();
            if(!ConsentValid(request.Consent,facts))blockers.Add("CONSENT_DOCUMENT_OR_PROVENANCE_REQUIRED");
            if(referenced.Any(id=>!facts.Security[id].Reviewable))blockers.Add("PROOF_EFFECTIVE_CLEAN_REQUIRED");
            if(blockers.Count>0)return Fail(Block(blockers[0]));
        }
        var selected=facts.Identity.Binding!.IdentityProofs.Concat(binding.AuthorityProofs).Select(p=>p.ProofId)
            .Concat(facts.Source?.Evidence?.IssuerProofs.Select(p=>p.ProofId)??[]).Concat(facts.Source?.Evidence?.IndependentChannelProofs.Select(p=>p.ProofId)??[])
            .Concat(identity.LastSourceCheck?.ManualCheck?.ConfirmationProofs.Select(p=>p.ProofId)??[]).Concat(referenced).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var links=facts.Proofs.Where(p=>selected.Contains(p.ProofId)).Select(p=>new QuoteReviewProof(p.ProofId,p.Sha256,p.EvidenceType,facts.Security[p.ProofId].ScanPolicyVersion??"MISSING",facts.Security[p.ProofId].ScannedAt??"MISSING")).ToArray();
        var now=clock.GetUtcNow();var decision=new QuoteIndependentReviewDecision("qreview_"+Guid.NewGuid().ToString("N"),w,c,q,caseRow.RootQuoteId,caseRow.DocumentId,caseRow.QuoteRevision,caseRow.IssuedPdfSha256,caseRow.CommercialFingerprint,
            binding.SubjectId,row.ResourceVersion,IdentityFingerprint(caseRow,facts.Identity),facts.BindingVersion,"identity-version:"+c+":"+facts.CheckVersion,facts.CheckVersion,binding.SourceId,binding.SourceVersion,SourceCheckFingerprint(facts.Identity),SourceDigest(facts.Source),links,request.Findings,request.Consent,request.Decision,request.Reason,actor,now.UtcDateTime.ToString("O"),facts.Policy,"Development",ReviewBindingFingerprint:ReviewFingerprint(context,facts));
        var next=facts.Identity with{ResourceVersion=row.ResourceVersion+1};var nextFacts=facts with{Identity=next};
        var view=View(context,nextFacts,decision);var id="qreview_cmd_"+Guid.NewGuid().ToString("N");var audit="qreview_audit_"+Guid.NewGuid().ToString("N");var evt="qreview_event_"+Guid.NewGuid().ToString("N");
        var response=new QuoteIdentityMutation<QuoteIndependentReviewView>(id,command.CorrelationId,c,"QUOTE_INDEPENDENT_REVIEW",next.ResourceVersion,now.UtcDateTime.ToString("O"),"COMMITTED",view,[evt],[audit]);
        var snapshot=JsonSerializer.SerializeToNode(next,QuotePricing.Json)!.AsObject();snapshot["independentReview"]=JsonSerializer.SerializeToNode(decision,QuotePricing.Json);
        store.AddVersion(new(w,c,q,next.ResourceVersion,"REVIEW_RECORDED",actor,request.Reason,snapshot.ToJsonString(QuotePricing.Json),facts.Policy,now));
        quotes.AddCommand(new(scope,w,actor,op,q,fingerprint,JsonSerializer.Serialize(response,QuotePricing.Json),audit,command.RequestId,command.CorrelationId,command.ExpectedVersion,next.ResourceVersion,now));
        quotes.AddOutbox(new(evt,w,q,"QUOTE_INDEPENDENT_REVIEW_RECORDED",command.CorrelationId,JsonSerializer.Serialize(new{decision.ReviewDecisionId,decision.CaseId,decision.Decision,decision.ReviewerId,decision.ReviewPolicyVersion,decision.AssuranceLevel,decision.ActualSourceVerification},QuotePricing.Json),now));
        await quotes.SaveChangesAsync(ct);probe.AfterFlush(w);await tx.CommitAsync(ct);
        return QuoteOperationResult<QuoteIdentityMutation<QuoteIndependentReviewView>>.Success(response);
    }
    private async Task<QuoteOperationResult<Facts>> FactsAsync(QuoteIdentityContext context,QuoteRequestMetadata meta,CancellationToken ct,QuoteAcceptancePartySnapshot? held=null,bool bytes=false)
    {
        var q=context.Grant.Quote;var c=context.Case!;var row=await store.CurrentAsync(q.WorkspaceId,c.CaseId,ct);var old=row is null?null:QuoteIdentityEvaluation.Decode<QuoteIdentityCaseView>(row.SnapshotJson);
        var assessment=await evaluation.AssessAsync(q,c,old?.Binding,meta,ct,held,old?.LastSourceCheck,verifyDocumentBytes:bytes);
        if(!assessment.IsSuccess)return QuoteOperationResult<Facts>.Failure(assessment.Error!);
        var identity=new QuoteIdentityCaseView(c.CaseId,q.QuoteId,row?.ResourceVersion??0,new(c.BuyerType,c.BuyerId),old?.Binding,assessment.Value!,old?.LastSourceCheck);
        var source=old?.Binding?.SourceId is {} id?await store.SourceAsync(q.WorkspaceId,id,ct):null;
        var all=await proofs.ProofsAsync(q.WorkspaceId,c.CaseId,0,6,ct);var clean=await scans.ReadAsync(q.WorkspaceId,all,false,ct);
        return QuoteOperationResult<Facts>.Success(new(identity,source is null?null:QuoteIdentityEvaluation.Decode<QuoteEvidenceSource>(source.SnapshotJson),all,clean,await quotes.ReadCreatorAsync(q.WorkspaceId,q.QuoteId,ct),policies.ReviewPolicy(q.WorkspaceId),(await store.LatestKindAsync(q.WorkspaceId,c.CaseId,"BINDING_RECORDED",ct))?.ResourceVersion??0,(await store.LatestKindAsync(q.WorkspaceId,c.CaseId,"SOURCE_CHECK_ATTEMPTED",ct))?.ResourceVersion??0));
    }
    private QuoteIndependentReviewView View(QuoteIdentityContext c,Facts f,QuoteIndependentReviewDecision? d)
    {
        var blockers=PositiveBlockers(f).ToList();var effective=blockers.Count==0;var status=effective?"REVIEW_PENDING":"REVIEW_BLOCKED";
        if(d is not null)
        {
            if(d.Decision!="APPROVE_SIMULATION")status=QuoteIndependentReviewRules.Status(d.Decision);
            else
            {
                effective=effective&&d.ReviewBindingFingerprint==ReviewFingerprint(c,f)&&d.AuthorityBindingVersion==f.BindingVersion&&d.SourceCheckVersion==f.CheckVersion&&d.ReviewPolicyVersion==f.Policy&&d.IdentityFingerprint==IdentityFingerprint(c.Case!,f.Identity)&&d.SourceCheckFingerprint==SourceCheckFingerprint(f.Identity)
                    &&d.SourceProvenanceDigest==SourceDigest(f.Source)&&Independent(c,f,d.ReviewerId)&&ConsentValid(d.Consent,f)
                    &&d.Proofs.All(l=>f.Proofs.Any(p=>p.ProofId==l.ProofId&&p.Sha256==l.Sha256)&&f.Security.TryGetValue(l.ProofId,out var s)&&s.Reviewable&&s.ScanPolicyVersion==l.ScanPolicyVersion&&s.ScannedAt==l.ScannedAt);
                status=effective?"REVIEW_APPROVED_SIMULATION":"REVIEW_INVALIDATED";
                if(!effective)blockers.Add("HISTORICAL_REVIEW_NO_LONGER_EFFECTIVE");
            }
        }
        if(!Independent(c,f,c.Grant.Grants[0].Trusted.MemberId))blockers.Add("REVIEWER_NOT_INDEPENDENT");
        return new(c.Case!.CaseId,c.Grant.Quote.QuoteId,f.Identity.ResourceVersion,status,blockers.Count==0,blockers.Distinct().ToArray(),f.Policy??"MISSING",f.Identity,new(c.Case.DocumentId,c.Case.QuoteRevision,c.Case.IssuedPdfSha256,c.Case.CommercialFingerprint),ReviewFingerprint(c,f),d,f.Policy is null?"UNVERIFIED":QuoteManualSourceWorkflow.Simulation,false);
    }
    private static IEnumerable<string> PositiveBlockers(Facts f)
    {
        if(f.Policy is null)yield return "INDEPENDENT_REVIEW_POLICY_UNAVAILABLE";
        foreach(var r in f.Identity.Assessment.Reasons)yield return r;
        if(!f.Proofs.Any(p=>p.EvidenceType=="CUSTOMER_CONSENT"&&f.Security[p.ProofId].Reviewable))yield return "CUSTOMER_CONSENT_CLEAN_PROOF_REQUIRED";
    }
    private bool ConsentValid(QuoteConsentReview? consent,Facts f)=>consent is {Finding:"EXPLICIT_UNCONDITIONAL_SIGNED_ASSENT",ProofIds.Length:>0}
        &&QuoteIdentityEvaluation.TryTime(consent.AcquiredAt,out var at)&&at<=clock.GetUtcNow()
        &&consent.ProofIds.All(id=>f.Proofs.Any(p=>p.ProofId==id&&p.EvidenceType=="CUSTOMER_CONSENT"&&at<=p.UploadedAt)&&f.Security.TryGetValue(id,out var s)&&s.Reviewable);
    private static bool Independent(QuoteIdentityContext c,Facts f,string actor)=>QuoteIndependentReviewRules.Independent(actor,f.Creator,c.Grant.Quote.ApprovalRequestedBy,c.Case!.CreatedBy,
        f.Proofs.Select(p=>p.UploadedBy),f.Identity.Binding?.RecordedBy,f.Source?.CreatedBy,f.Source?.EvidencePreparedBy,f.Identity.LastSourceCheck?.ActorId);
    private static string ReviewFingerprint(QuoteIdentityContext c,Facts f)=>QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new{
        c.Case!.CaseId,c.Case.DocumentId,c.Case.QuoteRevision,c.Case.IssuedPdfSha256,c.Case.CommercialFingerprint,c.Case.CreatedBy,
        c.Grant.Quote.ResourceVersion,c.Grant.Quote.Status,c.Grant.Quote.ArchivedAt,
        identity=IdentityFingerprint(c.Case,f.Identity),f.BindingVersion,f.CheckVersion,source=SourceDigest(f.Source),sourceCheck=SourceCheckFingerprint(f.Identity),f.Policy,f.Creator,
        eligibility=f.Identity.Assessment.Reasons.OrderBy(x=>x).ToArray(),
        proofs=f.Proofs.OrderBy(p=>p.ProofId).Select(p=>new{p.ProofId,p.Sha256,p.EvidenceType,p.UploadedBy,security=f.Security[p.ProofId]}).ToArray()
    },QuotePricing.Json));
    private static string IdentityFingerprint(QuoteAcceptanceCase c,QuoteIdentityCaseView v)=>v.Binding is null?"MISSING":QuoteManualSourceWorkflow.CheckFingerprint(c,v.Binding,v.Assessment.PolicyVersion);
    private static string SourceCheckFingerprint(QuoteIdentityCaseView v)=>QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(v.LastSourceCheck,QuotePricing.Json));
    private static string SourceDigest(QuoteEvidenceSource? s)=>QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(s,QuotePricing.Json));
    private static QuoteIndependentReviewDecision Decode(QuoteIdentityVersion r)=>JsonSerializer.Deserialize<QuoteIndependentReviewDecision>(JsonNode.Parse(r.SnapshotJson)!["independentReview"]!.ToJsonString(),QuotePricing.Json)??throw new InvalidOperationException("Stored review unavailable");
    private async Task AuditAsync(QuoteIdentityContext c,QuoteRequestMetadata m,string op,CancellationToken ct)
    {quotes.AddReadAudit(new(op,c.Grant.Quote.WorkspaceId,c.Grant.Grants[0].Trusted.MemberId,c.Case!.CaseId,m.RequestId,m.CorrelationId,null,clock.GetUtcNow()));await quotes.SaveChangesAsync(ct);}
    private static QuoteOperationError Input()=>new("INDEPENDENT_REVIEW_INPUT_INVALID",422,"Invalid independent review input");
    private static QuoteOperationError Block(string reason)=>new("QUOTE_INDEPENDENT_REVIEW_BLOCKED",409,"Independent review is blocked",reason);
    private static QuoteOperationResult<QuoteIdentityMutation<QuoteIndependentReviewView>> Fail(QuoteOperationError e)=>QuoteOperationResult<QuoteIdentityMutation<QuoteIndependentReviewView>>.Failure(e);
    private static QuoteOperationResult<QuoteIndependentReviewView> FailView(QuoteOperationError e)=>QuoteOperationResult<QuoteIndependentReviewView>.Failure(e);
}
