using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteEvidenceSourceService(QuoteIdentityAccess access,IQuoteIdentityPersistence store,IQuotesPersistence quotes,
    IEnumerable<IQuoteDocumentPartyReader> buyers,IQuoteIdentityCommitProbe probe,TimeProvider clock,QuoteManualSourceWorkflow manual,IQuoteAcceptancePartyReader parties)
{
    internal async Task<QuoteOperationResult<QuoteProofPage<QuoteEvidenceSource>>> ReadAsync(string q,string? sourceId,int offset,int limit,QuoteRequestMetadata meta,CancellationToken ct)
    {
        var a=await access.AuthorizeAsync(q,null,meta,ct);if(!a.IsSuccess)return QuoteOperationResult<QuoteProofPage<QuoteEvidenceSource>>.Failure(a.Error!);
        var quote=a.Value!.Grant.Quote;if(offset is <0 or >10000 || limit is <1 or >50)return QuoteOperationResult<QuoteProofPage<QuoteEvidenceSource>>.Failure(QuoteIdentityAccess.Input());
        await using var buyer=await BuyerAsync(quote.BuyerType,quote.BuyerId,meta,ct);if(buyer?.Snapshot is null)return QuoteOperationResult<QuoteProofPage<QuoteEvidenceSource>>.Failure(QuoteErrors.AccessDenied());
        IReadOnlyList<Domain.QuoteEvidenceSourceVersion> rows;
        if(sourceId is null)rows=await store.SourcesAsync(quote.WorkspaceId,quote.BuyerType,quote.BuyerId,offset,limit+1,ct);
        else
        {
            var found=await store.SourceAsync(quote.WorkspaceId,sourceId,ct);
            if(found is null || found.BuyerType!=quote.BuyerType || !string.Equals(found.BuyerId,quote.BuyerId,StringComparison.OrdinalIgnoreCase))return QuoteOperationResult<QuoteProofPage<QuoteEvidenceSource>>.Failure(QuoteErrors.NotFound());
            rows=await store.SourceHistoryAsync(quote.WorkspaceId,found.SourceId,offset,limit+1,ct);
        }
        quotes.AddReadAudit(new(sourceId is null?"listQuoteAcceptanceEvidenceSources":"listQuoteAcceptanceEvidenceSourceHistory",quote.WorkspaceId,a.Value.Grant.Grants[0].Trusted.MemberId,quote.QuoteId,meta.RequestId,meta.CorrelationId,null,clock.GetUtcNow()));await quotes.SaveChangesAsync(ct);
        return QuoteOperationResult<QuoteProofPage<QuoteEvidenceSource>>.Success(new(rows.Take(limit).Select(r=>Project(QuoteIdentityEvaluation.Decode<QuoteEvidenceSource>(r.SnapshotJson),quote.QuoteId,quote.WorkspaceId,a.Value.Grant.Grants[0].Trusted.MemberId)).ToArray(),new(rows.Count>limit,rows.Count>limit?(offset+limit).ToString(CultureInfo.InvariantCulture):null)));
    }
    internal async Task<QuoteOperationResult<QuoteIdentityMutation<QuoteEvidenceSource>>> WriteAsync(string q,string? sourceId,QuoteEvidenceSourceRequest? request,
        QuoteEvidenceSourceAction? action,string kind,QuoteCommandMetadata command,CancellationToken ct)
    {
        var meta=new QuoteRequestMetadata(command.RequestId,command.CorrelationId);var a=await access.AuthorizeAsync(q,null,meta,ct,QuoteCapabilities.ManageAcceptanceEvidenceSources,write:true);if(!a.IsSuccess)return Fail(a.Error!);
        if(kind is not ("CREATE" or "REVISE" or "REVOKE" or "APPROVE") || kind is "CREATE" or "REVISE" && !Valid(request)
            || kind is "REVOKE" or "APPROVE" && (action is null || string.IsNullOrWhiteSpace(action.Reason) || !QuoteIdentityCaseService.Text(action.Reason,500)))return Fail(QuoteIdentityAccess.Input());
        var context=a.Value!;var quote=context.Grant.Quote;var actor=context.Grant.Grants[0].Trusted.MemberId;var op=kind switch{"CREATE"=>"registerQuoteAcceptanceEvidenceSource","REVISE"=>"reviseQuoteAcceptanceEvidenceSource","REVOKE"=>"revokeQuoteAcceptanceEvidenceSource",_=>"approveQuoteAcceptanceEvidenceSource"};
        var scope=QuoteIdentityCaseService.Scope(quote.WorkspaceId,actor,op,sourceId??quote.QuoteId,command.IdempotencyKey);
        var fingerprint=QuoteIdentityCaseService.Fingerprint(new{request,action,sourceId,quoteId=quote.QuoteId},command.ExpectedVersion);
        await using var tx=await quotes.BeginAsync(ct);await quotes.LockAsync(scope,ct);await quotes.LockAsync($"aggregate:{quote.WorkspaceId}:{quote.QuoteId}",ct);
        var anticipated=sourceId is null?null:await store.SourceAsync(quote.WorkspaceId,sourceId,ct);
        var anticipatedCase=request?.Evidence?.CaseId??(anticipated is null?null:QuoteIdentityEvaluation.Decode<QuoteEvidenceSource>(anticipated.SnapshotJson).Evidence?.CaseId);
        if(anticipatedCase is not null)await quotes.LockAsync("identity-case:"+quote.WorkspaceId.ToLowerInvariant()+":"+anticipatedCase.ToLowerInvariant(),ct);
        if(sourceId is not null)await quotes.LockAsync("identity-source:"+quote.WorkspaceId.ToLowerInvariant()+":"+sourceId.ToLowerInvariant(),ct);
        a=await access.AuthorizeAsync(q,null,meta,ct,QuoteCapabilities.ManageAcceptanceEvidenceSources,write:true);if(!a.IsSuccess)return Fail(a.Error!);context=a.Value!;quote=context.Grant.Quote;
        var oldRow=sourceId is null?null:await store.SourceAsync(quote.WorkspaceId,sourceId,ct);
        if(sourceId is not null && (oldRow is null || oldRow.BuyerType!=quote.BuyerType || !string.Equals(oldRow.BuyerId,quote.BuyerId,StringComparison.OrdinalIgnoreCase)))return Fail(QuoteErrors.NotFound());
        var old=oldRow is null?null:QuoteIdentityEvaluation.Decode<QuoteEvidenceSource>(oldRow.SnapshotJson);
        var evidenceCase=request?.Evidence?.CaseId??old?.Evidence?.CaseId;
        if(evidenceCase!=anticipatedCase)return Fail(QuoteIdentityAccess.Stale());
        if(evidenceCase is not null)
        {
            var ca=await access.AuthorizeAsync(q,evidenceCase,meta,ct,QuoteCapabilities.ManageAcceptanceEvidenceSources,content:kind=="APPROVE",write:true);
            if(!ca.IsSuccess)return Fail(ca.Error!);
        }
        if(kind=="APPROVE")return await ApproveAsync(context,old!,action!,scope,fingerprint,op,actor,command,tx,meta,ct);
        await using var buyer=await BuyerAsync(quote.BuyerType,quote.BuyerId,meta,ct);if(buyer?.Snapshot is null)return Fail(QuoteErrors.AccessDenied());
        var receipt=await quotes.FindCommandAsync(scope,ct);
        if(receipt is not null)
        {
            if(receipt.Fingerprint!=fingerprint)return Fail(new("IDEMPOTENCY_KEY_REUSED",409,"Idempotency key reused"));
            var replay=QuoteIdentityEvaluation.Decode<QuoteIdentityMutation<QuoteEvidenceSource>>(receipt.ResponseJson);return QuoteOperationResult<QuoteIdentityMutation<QuoteEvidenceSource>>.Success(replay with{Outcome="REPLAYED"});
        }
        if(command.ExpectedVersion!=(oldRow?.ResourceVersion??0))return Fail(QuoteIdentityAccess.Stale());
        if(!buyer.Snapshot.Eligible || old?.Status=="REVOKED")return Fail(QuoteIdentityAccess.Block("SOURCE_NOT_MUTABLE"));
        if(kind=="REVISE" && string.IsNullOrWhiteSpace(request?.Reason))return Fail(QuoteIdentityAccess.Input());
        var now=clock.GetUtcNow();var id=old?.SourceId??"qsource_"+Guid.NewGuid().ToString("N");var version=checked((old?.ResourceVersion??0)+1);
        var source=kind=="REVOKE"?old! with{ResourceVersion=version,Status="REVOKED",ChangeReason=action!.Reason.Trim(),RecordedBy=actor,RecordedAt=now.UtcDateTime.ToString("O"),PolicyVersion=context.PolicyVersion!}
            :new QuoteEvidenceSource(id,version,new(quote.BuyerType,quote.BuyerId),request!.Name.Trim(),request.SourceType,request.IssuerReference.Trim(),request.VerificationMethod.Trim(),request.ProvenanceReference.Trim(),request.Requirements.Trim(),"DRAFT",old?.CreatedBy??actor,actor,now.UtcDateTime.ToString("O"),context.PolicyVersion!,request.Reason?.Trim()??"INITIAL_SOURCE_DRAFT");
        if(kind is "CREATE" or "REVISE")source=source with{EvidencePreparedBy=actor};
        if(kind is "CREATE" or "REVISE" && request?.Evidence is not null)
        {
            if(request.SourceType!="ORGANIZATION_ISSUED"||request.VerificationMethod!=QuoteManualSourceWorkflow.Method)return Fail(QuoteIdentityAccess.Block("SOURCE_METHOD_NOT_ADMITTED"));
            var evidence=await manual.EvidenceAsync(quote,request.Evidence,ct);if(!evidence.IsSuccess)return Fail(evidence.Error!);
            source=source with{Evidence=evidence.Value!,AdmissionBlocker="INDEPENDENT_SOURCE_APPROVAL_REQUIRED"};
        }
        return await CommitAsync(context,source,kind,scope,fingerprint,op,actor,command,tx,ct);
    }
    private async Task<QuoteOperationResult<QuoteIdentityMutation<QuoteEvidenceSource>>> ApproveAsync(QuoteIdentityContext context,QuoteEvidenceSource old,QuoteEvidenceSourceAction action,
        string scope,string fingerprint,string op,string actor,QuoteCommandMetadata command,IQuoteTransaction tx,QuoteRequestMetadata meta,CancellationToken ct)
    {
        var quote=context.Grant.Quote;
        if(old.CreatedBy==actor||(old.EvidencePreparedBy??old.RecordedBy)==actor)return Fail(new("ACCESS_DENIED",403,"Independent source admission required","SELF_SOURCE_ADMISSION_FORBIDDEN"));
        if(!manual.Enabled(quote.WorkspaceId)||old.Evidence is null||old.VerificationMethod!=QuoteManualSourceWorkflow.Method)return Fail(QuoteIdentityAccess.Block("BLOCKED_CONTRACT_SOURCE_METHOD_NOT_ADMITTED"));
        var(c,v)=await manual.CaseAsync(quote,old.Evidence.CaseId,ct);if(c is null||v?.Binding is not {} binding)return Fail(QuoteIdentityAccess.Block("SOURCE_CASE_BINDING_REQUIRED"));
        var creator=await quotes.ReadCreatorAsync(quote.WorkspaceId,quote.QuoteId,ct);
        if(creator is null||actor==creator||actor==binding.RecordedBy||actor==quote.ApprovalRequestedBy)return Fail(new("ACCESS_DENIED",403,"Independent source admission required","SELF_SOURCE_ADMISSION_FORBIDDEN"));
        await using var held=await parties.AcquireAsync(c.BuyerType,c.BuyerId,binding.Party.ContactId,binding.Party.RelationshipId,meta.RequestId,meta.CorrelationId,ct);
        if(held.Snapshot is not {} party||party.WorkspaceId!=quote.WorkspaceId)return Fail(QuoteErrors.AccessDenied());
        var replay=await quotes.FindCommandAsync(scope,ct);
        if(replay is not null){if(replay.Fingerprint!=fingerprint)return Fail(new("IDEMPOTENCY_KEY_REUSED",409,"Idempotency key reused"));return QuoteOperationResult<QuoteIdentityMutation<QuoteEvidenceSource>>.Success(QuoteIdentityEvaluation.Decode<QuoteIdentityMutation<QuoteEvidenceSource>>(replay.ResponseJson) with{Outcome="REPLAYED"});}
        if(command.ExpectedVersion!=old.ResourceVersion)return Fail(QuoteIdentityAccess.Stale());
        if(old.Status!="DRAFT"||old.PolicyVersion!=context.PolicyVersion||!party.Eligible||party.ContactVersion!=binding.Party.ContactVersion||party.BuyerVersion!=binding.Party.BuyerVersion
            ||!await manual.CurrentEvidenceAsync(quote,c,binding,old.Evidence,ct)|| (await manual.CommercialReasonsAsync(quote,c,ct)).Count>0)return Fail(QuoteIdentityAccess.Block("SOURCE_ADMISSION_EVIDENCE_OR_POLICY_INVALID"));
        var source=old with{ResourceVersion=old.ResourceVersion+1,Status="APPROVED",ApprovedBy=actor,ApprovedAt=clock.GetUtcNow().UtcDateTime.ToString("O"),RecordedBy=actor,
            RecordedAt=clock.GetUtcNow().UtcDateTime.ToString("O"),ChangeReason=action.Reason.Trim(),AssuranceLevel=QuoteManualSourceWorkflow.Simulation,CanApprove=false,AdmissionBlocker="DEVELOPMENT_SIMULATION_ONLY"};
        return await CommitAsync(context,source,"APPROVE",scope,fingerprint,op,actor,command,tx,ct);
    }
    private async Task<QuoteOperationResult<QuoteIdentityMutation<QuoteEvidenceSource>>> CommitAsync(QuoteIdentityContext context,QuoteEvidenceSource source,string kind,
        string scope,string fingerprint,string op,string actor,QuoteCommandMetadata command,IQuoteTransaction tx,CancellationToken ct)
    {
        var quote=context.Grant.Quote;var id=source.SourceId;var version=source.ResourceVersion;var now=clock.GetUtcNow();
        store.AddSource(new(quote.WorkspaceId,id,version,quote.BuyerType,quote.BuyerId,source.Status,actor,JsonSerializer.Serialize(source,QuotePricing.Json),now));
        var cmd="qis_cmd_"+Guid.NewGuid().ToString("N");var audit="qis_audit_"+Guid.NewGuid().ToString("N");var evt="qis_event_"+Guid.NewGuid().ToString("N");
        var response=new QuoteIdentityMutation<QuoteEvidenceSource>(cmd,command.CorrelationId,id,"QUOTE_ACCEPTANCE_SOURCE",version,now.UtcDateTime.ToString("O"),"COMMITTED",source,[evt],[audit]);
        quotes.AddCommand(new(scope,quote.WorkspaceId,actor,op,quote.QuoteId,fingerprint,JsonSerializer.Serialize(response,QuotePricing.Json),audit,command.RequestId,command.CorrelationId,command.ExpectedVersion,version,now));
        quotes.AddOutbox(new(evt,quote.WorkspaceId,quote.QuoteId,"ACCEPTANCE_SOURCE_"+kind,command.CorrelationId,JsonSerializer.Serialize(new{sourceId=id,version,source.Status,actor,policyVersion=context.PolicyVersion},QuotePricing.Json),now));
        await quotes.SaveChangesAsync(ct);probe.AfterFlush(quote.WorkspaceId);await tx.CommitAsync(ct);return QuoteOperationResult<QuoteIdentityMutation<QuoteEvidenceSource>>.Success(response);
    }
    private QuoteEvidenceSource Project(QuoteEvidenceSource s,string q,string w,string actor)=>s.Evidence is {} e&&e.QuoteId!=q?s with{Evidence=null,CanApprove=false}:s with{CanApprove=manual.Enabled(w)&&s.Status=="DRAFT"&&s.Evidence is not null&&s.CreatedBy!=actor&&(s.EvidencePreparedBy??s.RecordedBy)!=actor&&s.VerificationMethod==QuoteManualSourceWorkflow.Method};
    private async Task<IQuoteDocumentPartyLease?> BuyerAsync(string type,string id,QuoteRequestMetadata meta,CancellationToken ct)
    {var reader=buyers.SingleOrDefault(x=>x.BuyerType==type);return reader is null?null:await reader.AcquireAsync(id,meta.RequestId,meta.CorrelationId,ct);}
    private static bool Valid(QuoteEvidenceSourceRequest? r)=>r is not null
        && r.SourceType is "AUTHORITATIVE_REGISTRY" or "ORGANIZATION_ISSUED" or "INTERNAL_PROCEDURE" or "TRUSTED_EXTERNAL_PROVIDER"
        && new[]{r.Name,r.IssuerReference,r.VerificationMethod,r.ProvenanceReference,r.Requirements}.All(x=>!string.IsNullOrWhiteSpace(x))
        && QuoteIdentityCaseService.Text(r.Name,120) && QuoteIdentityCaseService.Text(r.IssuerReference,200) && QuoteIdentityCaseService.Text(r.VerificationMethod,100)
        && QuoteIdentityCaseService.Text(r.ProvenanceReference,200) && QuoteIdentityCaseService.Text(r.Requirements,1000) && QuoteIdentityCaseService.Text(r.Reason,500);
    private static QuoteOperationResult<QuoteIdentityMutation<QuoteEvidenceSource>> Fail(QuoteOperationError e)=>QuoteOperationResult<QuoteIdentityMutation<QuoteEvidenceSource>>.Failure(e);
}
