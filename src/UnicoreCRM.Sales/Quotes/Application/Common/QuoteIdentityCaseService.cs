using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteIdentityCaseService(QuoteIdentityAccess access,QuoteIdentityEvaluation evaluation,IQuoteIdentityPersistence store,
    IQuotesPersistence quotes,IQuoteAcceptancePartyReader parties,QuoteProofEffectiveScan scans,IQuoteIdentityCommitProbe probe,TimeProvider clock,QuoteManualSourceWorkflow manual)
{
    internal async Task<QuoteOperationResult<QuoteIdentityCaseView>> ReadAsync(string q,string c,QuoteRequestMetadata meta,CancellationToken ct)
    {
        var a=await access.AuthorizeAsync(q,c,meta,ct);if(!a.IsSuccess)return QuoteOperationResult<QuoteIdentityCaseView>.Failure(a.Error!);
        var result=await evaluation.ViewAsync(a.Value!.Grant.Quote,a.Value.Case!,meta,ct);if(result.IsSuccess)await AuditReadAsync(a.Value,meta,"getQuoteIdentityAuthority",ct);return result;
    }
    internal async Task<QuoteOperationResult<QuoteProofPage<QuoteIdentityHistoryItem>>> HistoryAsync(string q,string c,int offset,int limit,QuoteRequestMetadata meta,CancellationToken ct)
    {
        var a=await access.AuthorizeAsync(q,c,meta,ct);if(!a.IsSuccess)return QuoteOperationResult<QuoteProofPage<QuoteIdentityHistoryItem>>.Failure(a.Error!);
        if(offset is <0 or >10000 || limit is <1 or >50)return QuoteOperationResult<QuoteProofPage<QuoteIdentityHistoryItem>>.Failure(QuoteIdentityAccess.Input());
        var current=await evaluation.ViewAsync(a.Value!.Grant.Quote,a.Value.Case!,meta,ct);if(!current.IsSuccess)return QuoteOperationResult<QuoteProofPage<QuoteIdentityHistoryItem>>.Failure(current.Error!);
        var rows=await store.HistoryAsync(a.Value.Grant.Quote.WorkspaceId,a.Value.Case!.CaseId,offset,limit+1,ct);
        // Historical names/CRM IDs are not disclosed through this summary; no per-history Contact lookup.
        var items=rows.Take(limit).Select(row=>{var view=QuoteIdentityEvaluation.Decode<QuoteIdentityCaseView>(row.SnapshotJson);return new QuoteIdentityHistoryItem(row.ResourceVersion,row.Kind,row.ActorId,row.RecordedAt.UtcDateTime.ToString("O"),row.Reason,view.Assessment,view.Binding?.SubjectId,view.LastSourceCheck);}).ToArray();
        await AuditReadAsync(a.Value,meta,"listQuoteIdentityAuthorityHistory",ct);
        return QuoteOperationResult<QuoteProofPage<QuoteIdentityHistoryItem>>.Success(new(items,new(rows.Count>limit,rows.Count>limit?(offset+limit).ToString(CultureInfo.InvariantCulture):null)));
    }
    internal async Task<QuoteOperationResult<QuoteIdentityMutation<QuoteIdentityCaseView>>> SaveAsync(string q,string c,SaveQuoteIdentityBindingRequest request,QuoteCommandMetadata command,CancellationToken ct)
    {
        var meta=new QuoteRequestMetadata(command.RequestId,command.CorrelationId);
        var a=await access.AuthorizeAsync(q,c,meta,ct,submit:true,write:true);if(!a.IsSuccess)return Fail(a.Error!);
        if(!Valid(request))return Fail(QuoteIdentityAccess.Input());
        var context=a.Value!;var quote=context.Grant.Quote;var row=context.Case!;var actor=context.Grant.Grants[0].Trusted.MemberId;
        var scope=Scope(quote.WorkspaceId,actor,"saveQuoteIdentityAuthorityBinding",row.CaseId,command.IdempotencyKey);var fingerprint=Fingerprint(request,command.ExpectedVersion);
        await using var tx=await quotes.BeginAsync(ct);await quotes.LockAsync(scope,ct);await quotes.LockAsync($"aggregate:{quote.WorkspaceId}:{quote.QuoteId}",ct);await quotes.LockAsync("identity-case:"+quote.WorkspaceId.ToLowerInvariant()+":"+row.CaseId.ToLowerInvariant(),ct);
        a=await access.AuthorizeAsync(q,c,meta,ct,submit:true,write:true);if(!a.IsSuccess)return Fail(a.Error!);context=a.Value!;quote=context.Grant.Quote;row=context.Case!;
        if(request.SourceId is not null)await quotes.LockAsync("identity-source:"+quote.WorkspaceId.ToLowerInvariant()+":"+request.SourceId.ToLowerInvariant(),ct);
        await using var held=await parties.AcquireAsync(row.BuyerType,row.BuyerId,request.ContactId,request.RelationshipId,meta.RequestId,meta.CorrelationId,ct);
        var party=held.Snapshot;if(party is null || party.WorkspaceId!=quote.WorkspaceId)return Fail(QuoteErrors.AccessDenied());
        var replay=await ReplayAsync(scope,fingerprint,party,context,meta,ct);if(replay is not null)return replay;
        if(!party.Eligible)return Fail(QuoteIdentityAccess.Block("CRM_PARTY_OR_AFFILIATION_INACTIVE"));
        var oldRow=await store.CurrentAsync(quote.WorkspaceId,row.CaseId,ct);var before=oldRow?.ResourceVersion??0;
        if(before!=command.ExpectedVersion)return Fail(QuoteIdentityAccess.Stale());var old=oldRow is null?null:QuoteIdentityEvaluation.Decode<QuoteIdentityCaseView>(oldRow.SnapshotJson);
        if(request.ExpectedSubjectId!=old?.Binding?.SubjectId)return Fail(QuoteIdentityAccess.Block("IDENTITY_SUBJECT_MISMATCH"));
        if(old?.Binding is not null && string.IsNullOrWhiteSpace(request.Reason))return Fail(QuoteIdentityAccess.Input());
        var ids=request.IdentityProofIds.Concat(request.AuthorityProofIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();var proofs=await store.ProofsAsync(quote.WorkspaceId,row.CaseId,ids,ct);
        if(proofs.Count!=ids.Length)return Fail(QuoteErrors.NotFound());
        var clean=await scans.ReadAsync(quote.WorkspaceId,proofs,false,ct);if(proofs.Any(p=>!clean[p.ProofId].Reviewable))return Fail(QuoteIdentityAccess.Block("PROOF_EFFECTIVE_CLEAN_REQUIRED"));
        QuoteIdentityProofLink[] Links(IEnumerable<string> keys)
        {return keys.Select(id=>{var p=proofs.Single(p=>p.ProofId.Equals(id,StringComparison.OrdinalIgnoreCase));return new QuoteIdentityProofLink(p.ProofId,p.Sha256,p.EvidenceType);}).ToArray();}
        var identity=Links(request.IdentityProofIds);var authority=Links(request.AuthorityProofIds);
        if(identity.Any(p=>p.EvidenceType is not ("IDENTITY" or "SUPPORTING_DOCUMENT")) || authority.Any(p=>p.EvidenceType is not ("DELEGATION" or "SUPPORTING_DOCUMENT")))return Fail(QuoteIdentityAccess.Input());
        string? sourceId=null;
        if(request.SourceId is not null)
        {
            var source=await store.SourceAsync(quote.WorkspaceId,request.SourceId,ct);
            if(source is null || source.BuyerType!=row.BuyerType || !string.Equals(source.BuyerId,row.BuyerId,StringComparison.OrdinalIgnoreCase))return Fail(QuoteErrors.NotFound());
            if(source.ResourceVersion!=request.SourceVersion)return Fail(QuoteIdentityAccess.Stale());sourceId=source.SourceId;
        }
        var now=clock.GetUtcNow();var subject=old?.Binding?.Party.ContactId==party.ContactId?old.Binding.SubjectId:"qsubject_"+Guid.NewGuid().ToString("N");
        var binding=new QuoteIdentityBinding(subject,party,identity,authority,request.AuthorityClaim??new(),sourceId,request.SourceVersion,actor,now.UtcDateTime.ToString("O"),identity.Length+authority.Length>0?"DOCUMENT_SUBMITTED":"SELF_REPORTED");
        var assessed=await evaluation.AssessAsync(quote,row,binding,meta,ct,party);if(!assessed.IsSuccess)return Fail(assessed.Error!);
        var view=new QuoteIdentityCaseView(row.CaseId,row.QuoteId,before+1,new(row.BuyerType,row.BuyerId),binding,assessed.Value!);
        return await CommitAsync(context,view,"BINDING_RECORDED",request.Reason??"INITIAL_BINDING",scope,fingerprint,"saveQuoteIdentityAuthorityBinding",actor,command,tx,ct);
    }
    internal async Task<QuoteOperationResult<QuoteIdentityMutation<QuoteIdentityCaseView>>> ActAsync(string q,string c,QuoteIdentityActionRequest request,bool sourceCheck,QuoteCommandMetadata command,CancellationToken ct)
    {
        var meta=new QuoteRequestMetadata(command.RequestId,command.CorrelationId);var cap=sourceCheck?QuoteCapabilities.RecordAcceptanceSourceCheck:QuoteCapabilities.AssessAcceptanceIdentityAuthority;
        var a=await access.AuthorizeAsync(q,c,meta,ct,cap,content:sourceCheck,write:true);if(!a.IsSuccess)return Fail(a.Error!);
        if(!QuotePricing.EntityId(request.SubjectId) || !Text(request.Reason,500))return Fail(QuoteIdentityAccess.Input());
        var context=a.Value!;var w=context.Grant.Quote.WorkspaceId;var actor=context.Grant.Grants[0].Trusted.MemberId;var op=sourceCheck?"recordQuoteAcceptanceSourceCheck":"assessQuoteIdentityAuthority";
        var scope=Scope(w,actor,op,context.Case!.CaseId,command.IdempotencyKey);var fingerprint=Fingerprint(request,command.ExpectedVersion);
        await using var tx=await quotes.BeginAsync(ct);await quotes.LockAsync(scope,ct);await quotes.LockAsync($"aggregate:{w}:{context.Grant.Quote.QuoteId}",ct);await quotes.LockAsync("identity-case:"+w.ToLowerInvariant()+":"+context.Case.CaseId.ToLowerInvariant(),ct);
        a=await access.AuthorizeAsync(q,c,meta,ct,cap,content:sourceCheck,write:true);if(!a.IsSuccess)return Fail(a.Error!);context=a.Value!;
        var oldRow=await store.CurrentAsync(w,context.Case!.CaseId,ct);if(oldRow is null)return Fail(QuoteIdentityAccess.Block("IDENTITY_BINDING_REQUIRED"));
        var old=QuoteIdentityEvaluation.Decode<QuoteIdentityCaseView>(oldRow.SnapshotJson);var binding=old.Binding;if(binding is null)return Fail(QuoteIdentityAccess.Block("IDENTITY_BINDING_REQUIRED"));
        QuoteEvidenceSource? source=null;
        if(binding.SourceId is not null)
        {
            await quotes.LockAsync("identity-source:"+w.ToLowerInvariant()+":"+binding.SourceId.ToLowerInvariant(),ct);var record=await store.SourceAsync(w,binding.SourceId,ct);
            if(record is not null)source=QuoteIdentityEvaluation.Decode<QuoteEvidenceSource>(record.SnapshotJson);
            if(sourceCheck && source is not null && (source.CreatedBy==actor || source.ApprovedBy==actor || source.EvidencePreparedBy==actor))return Fail(new("ACCESS_DENIED",403,"Independent source checker required","SELF_SOURCE_CHECK_FORBIDDEN"));
        }
        await using var held=await parties.AcquireAsync(context.Case.BuyerType,context.Case.BuyerId,binding.Party.ContactId,binding.Party.RelationshipId,meta.RequestId,meta.CorrelationId,ct);
        if(held.Snapshot is not {} party || party.WorkspaceId!=w)return Fail(QuoteErrors.AccessDenied());
        if(sourceCheck)
        {
            var creator=await quotes.ReadCreatorAsync(w,context.Case.QuoteId,ct);
            if(creator is null || actor==creator || actor==binding.RecordedBy || actor==context.Grant.Quote.ApprovalRequestedBy)return Fail(new("ACCESS_DENIED",403,"Independent source checker required","SELF_SOURCE_CHECK_FORBIDDEN"));
        }
        var replay=await ReplayAsync(scope,fingerprint,party,context,meta,ct);if(replay is not null)return replay;
        if(command.ExpectedVersion!=oldRow.ResourceVersion)return Fail(QuoteIdentityAccess.Stale());
        if(request.SubjectId!=binding.SubjectId)return Fail(QuoteIdentityAccess.Block("IDENTITY_SUBJECT_MISMATCH"));
        var assessed=await evaluation.AssessAsync(context.Grant.Quote,context.Case,binding,meta,ct,party,sourceCheck?null:old.LastSourceCheck);if(!assessed.IsSuccess)return Fail(assessed.Error!);
        if(sourceCheck && assessed.Value!.Reasons.Any(x=>x is "PROOF_EFFECTIVE_CLEAN_REQUIRED" or "IDENTITY_PROOF_REQUIRED" or "AUTHORITY_PROOF_REQUIRED" or "CRM_PARTY_SNAPSHOT_STALE" or "CRM_PARTY_OR_AFFILIATION_INACTIVE" or "SOURCE_VERSION_CHANGED" or "SOURCE_BUYER_BINDING_INVALID" or "SOURCE_REVOKED"))return Fail(QuoteIdentityAccess.Block("SOURCE_CHECK_INPUT_INELIGIBLE"));
        var check=sourceCheck?new QuoteIdentitySourceCheck("SOURCE_UNAVAILABLE",source?.VerificationMethod??"UNCONFIGURED",clock.GetUtcNow().UtcDateTime.ToString("O"),actor,context.PolicyVersion!,
            binding.IdentityProofs.Concat(binding.AuthorityProofs).DistinctBy(x=>x.ProofId).ToArray(),["BLOCKED_CONTRACT_IDENTITY_AUTHORITY_SOURCE"],binding.SourceId,binding.SourceVersion):old.LastSourceCheck;
        if(sourceCheck && request.ManualCheck is not null)
        {
            if(assessed.Value!.Reasons.Any(x=>x!="SOURCE_CHECK_REQUIRED"))return Fail(QuoteIdentityAccess.Block("SOURCE_CHECK_CURRENT_CASE_INELIGIBLE"));
            var recorded=await manual.CheckAsync(context.Grant.Quote,context.Case,binding,source,request.ManualCheck,actor,context.PolicyVersion!,ct);if(!recorded.IsSuccess)return Fail(recorded.Error!);check=recorded.Value!;
            assessed=await evaluation.AssessAsync(context.Grant.Quote,context.Case,binding,meta,ct,party,check);if(!assessed.IsSuccess)return Fail(assessed.Error!);
        }
        var view=old with{ResourceVersion=oldRow.ResourceVersion+1,Assessment=assessed.Value!,LastSourceCheck=check};
        return await CommitAsync(context,view,sourceCheck?"SOURCE_CHECK_ATTEMPTED":"ASSESSMENT_RECORDED",request.Reason??"USER_REQUESTED",scope,fingerprint,op,actor,command,tx,ct);
    }
    private async Task<QuoteOperationResult<QuoteIdentityMutation<QuoteIdentityCaseView>>?> ReplayAsync(string scope,string fingerprint,QuoteAcceptancePartySnapshot authorizedParty,QuoteIdentityContext context,QuoteRequestMetadata meta,CancellationToken ct)
    {
        var record=await quotes.FindCommandAsync(scope,ct);if(record is null)return null;
        if(record.Fingerprint!=fingerprint)return Fail(new("IDEMPOTENCY_KEY_REUSED",409,"Idempotency key reused"));
        var result=QuoteIdentityEvaluation.Decode<QuoteIdentityMutation<QuoteIdentityCaseView>>(record.ResponseJson);
        // A replay may contain an earlier subject. Do not disclose that historical party under a different current authorization.
        if(result.Result.Binding?.Party.ContactId!=authorizedParty.ContactId || result.Result.Binding?.Party.RelationshipId!=authorizedParty.RelationshipId)return Fail(QuoteErrors.AccessDenied());
        var current=await store.CurrentAsync(context.Grant.Quote.WorkspaceId,context.Case!.CaseId,ct);
        var view=current is null?result.Result:QuoteIdentityEvaluation.Decode<QuoteIdentityCaseView>(current.SnapshotJson);
        if(view.Binding?.Party.ContactId!=authorizedParty.ContactId||view.Binding?.Party.RelationshipId!=authorizedParty.RelationshipId)return Fail(QuoteErrors.AccessDenied());
        var assessed=await evaluation.AssessAsync(context.Grant.Quote,context.Case,view.Binding,meta,ct,authorizedParty,view.LastSourceCheck);if(!assessed.IsSuccess)return Fail(assessed.Error!);
        return QuoteOperationResult<QuoteIdentityMutation<QuoteIdentityCaseView>>.Success(result with{Outcome="REPLAYED",Result=view with{Assessment=assessed.Value!}});
    }
    private async Task<QuoteOperationResult<QuoteIdentityMutation<QuoteIdentityCaseView>>> CommitAsync(QuoteIdentityContext c,QuoteIdentityCaseView view,string kind,string reason,
        string scope,string fingerprint,string op,string actor,QuoteCommandMetadata command,IQuoteTransaction tx,CancellationToken ct)
    {
        var now=clock.GetUtcNow();var id="qia_cmd_"+Guid.NewGuid().ToString("N");var audit="qia_audit_"+Guid.NewGuid().ToString("N");var evt="qia_event_"+Guid.NewGuid().ToString("N");
        var response=new QuoteIdentityMutation<QuoteIdentityCaseView>(id,command.CorrelationId,view.CaseId,"QUOTE_IDENTITY_AUTHORITY",view.ResourceVersion,now.UtcDateTime.ToString("O"),"COMMITTED",view,[evt],[audit]);
        store.AddVersion(new(c.Grant.Quote.WorkspaceId,view.CaseId,view.QuoteId,view.ResourceVersion,kind,actor,reason,JsonSerializer.Serialize(view,QuotePricing.Json),c.PolicyVersion!,now));
        quotes.AddCommand(new(scope,c.Grant.Quote.WorkspaceId,actor,op,view.QuoteId,fingerprint,JsonSerializer.Serialize(response,QuotePricing.Json),audit,command.RequestId,command.CorrelationId,command.ExpectedVersion,view.ResourceVersion,now));
        quotes.AddOutbox(new(evt,c.Grant.Quote.WorkspaceId,view.QuoteId,kind,command.CorrelationId,JsonSerializer.Serialize(new{view.CaseId,view.ResourceVersion,subjectId=view.Binding?.SubjectId,policyVersion=c.PolicyVersion,actor},QuotePricing.Json),now));
        await quotes.SaveChangesAsync(ct);probe.AfterFlush(c.Grant.Quote.WorkspaceId);await tx.CommitAsync(ct);return QuoteOperationResult<QuoteIdentityMutation<QuoteIdentityCaseView>>.Success(response);
    }
    private async Task AuditReadAsync(QuoteIdentityContext c,QuoteRequestMetadata meta,string op,CancellationToken ct)
    {quotes.AddReadAudit(new(op,c.Grant.Quote.WorkspaceId,c.Grant.Grants[0].Trusted.MemberId,c.Case!.CaseId,meta.RequestId,meta.CorrelationId,null,clock.GetUtcNow()));await quotes.SaveChangesAsync(ct);}
    internal static bool Text(string? s,int maximum)=>s is null || s.Length<=maximum && !s.Any(char.IsControl);
    private static bool Valid(SaveQuoteIdentityBindingRequest r)=>QuotePricing.EntityId(r.ContactId) && (r.RelationshipId is null || QuotePricing.EntityId(r.RelationshipId))
        && r.IdentityProofIds is {Length:<=5} && r.AuthorityProofIds is {Length:<=5} && r.IdentityProofIds.All(QuotePricing.EntityId) && r.AuthorityProofIds.All(QuotePricing.EntityId)
        && r.IdentityProofIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()==r.IdentityProofIds.Length && r.AuthorityProofIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()==r.AuthorityProofIds.Length
        && (r.SourceId is null && r.SourceVersion is null || QuotePricing.EntityId(r.SourceId) && r.SourceVersion is >=1)
        && Text(r.Reason,500) && (r.AuthorityClaim is null || Text(r.AuthorityClaim.ScopeDescription,500) && Text(r.AuthorityClaim.Currency,3) && Text(r.AuthorityClaim.AmountLimit,40)
            && (r.AuthorityClaim.AmountLimit is null || decimal.TryParse(r.AuthorityClaim.AmountLimit,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var amount) && amount>=0)
            && (r.AuthorityClaim.EffectiveFrom is null || QuoteIdentityEvaluation.TryTime(r.AuthorityClaim.EffectiveFrom,out _)) && (r.AuthorityClaim.EffectiveUntil is null || QuoteIdentityEvaluation.TryTime(r.AuthorityClaim.EffectiveUntil,out _)));
    internal static string Scope(string w,string actor,string op,string aggregate,string key)=>QuoteApprovalAssessment.Hash($"{w}|{actor}|{op}|{aggregate.ToLowerInvariant()}|{key}");
    internal static string Fingerprint<T>(T r,long? version)=>QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new{r,version},QuotePricing.Json));
    private static QuoteOperationResult<QuoteIdentityMutation<QuoteIdentityCaseView>> Fail(QuoteOperationError e)=>QuoteOperationResult<QuoteIdentityMutation<QuoteIdentityCaseView>>.Failure(e);
}
