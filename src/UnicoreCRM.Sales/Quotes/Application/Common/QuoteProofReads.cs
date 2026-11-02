using System.Globalization;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteProofReads(QuoteProofAccess access,IQuoteProofPersistence proofs,IQuotesPersistence quotes,QuoteProofEffectiveScan effective,TimeProvider clock)
{
    internal async Task<QuoteOperationResult<object>> ReadAsync(string quoteId,string? caseId,string? proofId,bool security,bool single,string? cursor,int limit,
        QuoteRequestMetadata metadata,CancellationToken ct)
    {
        var grant=await access.AuthorizeAsync(quoteId,false,metadata,ct);if(!grant.IsSuccess)return Fail(grant.Error!);
        var w=grant.Value!.Quote.WorkspaceId;
        var offset=0;
        if(limit is <1 or >50 || cursor is not null && (!int.TryParse(cursor,NumberStyles.None,CultureInfo.InvariantCulture,out offset) || offset is <0 or >10000))
            return Fail(QuoteErrors.Validation(new Dictionary<string,string[]>{["pagination"]=["Bounded pagination required"]}));
        object result;
        if(caseId is null)
        {
            var rows=await proofs.CasesAsync(w,quoteId,offset,limit+1,ct);
            foreach(var row in rows.Take(limit))if(await access.StoredAsync(grant.Value,row,metadata,ct) is {} denied)return Fail(denied);
            result=new QuoteProofPage<QuoteAcceptanceCaseMetadata>(rows.Take(limit).Select(QuoteProofCommands.CaseMetadata).ToArray(),Page(rows.Count>limit,offset,limit));
        }
        else
        {
            var row=await proofs.CaseAsync(w,quoteId,caseId,false,ct);if(row is null)return Fail(QuoteErrors.NotFound());
            if(await access.StoredAsync(grant.Value,row,metadata,ct) is {} denied)return Fail(denied);
            if(proofId is not null)
            {
                var proof=await proofs.ProofAsync(w,row.CaseId,proofId,ct);if(proof is null)return Fail(QuoteErrors.NotFound());
                var states=await StatesAsync(quoteId,row,[proof],metadata,ct);var state=states[proof.ProofId];
                result=security?state:Overlay(proof,quoteId,state);
            }
            else if(single)result=QuoteProofCommands.CaseMetadata(row);
            else
            {
                var rows=await proofs.ProofsAsync(w,row.CaseId,offset,limit+1,ct);
                var page=rows.Take(limit).ToArray();var states=await StatesAsync(quoteId,row,page,metadata,ct);
                result=new QuoteProofPage<QuoteAcceptanceProofMetadata>(page.Select(p=>Overlay(p,quoteId,states[p.ProofId])).ToArray(),Page(rows.Count>limit,offset,limit));
            }
        }
        quotes.AddReadAudit(new("quoteAcceptanceProofMetadata",w,grant.Value.Grants[0].Trusted.MemberId,caseId??quoteId,metadata.RequestId,metadata.CorrelationId,null,clock.GetUtcNow()));
        await quotes.SaveChangesAsync(ct);
        return QuoteOperationResult<object>.Success(result);
    }
    private async Task<IReadOnlyDictionary<string,QuoteProofSecurity>> StatesAsync(string quoteId,Domain.QuoteAcceptanceCase row,IReadOnlyList<Domain.QuoteAcceptanceProof> page,QuoteRequestMetadata meta,CancellationToken ct)
    {
        var states=await effective.ReadAsync(row.WorkspaceId,page,false,ct);
        if(states.Values.Any(s=>s.Reviewable))
        {
            var content=await access.AuthorizeAsync(quoteId,false,meta,ct,content:true);
            if(content.IsSuccess && await access.StoredAsync(content.Value!,row,meta,ct)is null)
                return states.ToDictionary(x=>x.Key,x=>x.Value with{ContentAvailable=x.Value.Reviewable},StringComparer.Ordinal);
        }
        return states;
    }
    private static QuoteAcceptanceProofMetadata Overlay(Domain.QuoteAcceptanceProof p,string q,QuoteProofSecurity s)=>QuoteProofCommands.ProofMetadata(p,q) with
        {ScanStatus=s.ScanStatus,ContentAvailable=s.ContentAvailable,StructuralStatus=s.StructuralStatus,MalwareVerdict=s.MalwareVerdict,ScanReasonCode=s.ReasonCode,
        ScannedAt=s.ScannedAt,ScanPolicyVersion=s.ScanPolicyVersion,Reviewable=s.Reviewable};
    private static QuoteProofPageInfo Page(bool next,int offset,int limit)=>new(next,next?(offset+limit).ToString(CultureInfo.InvariantCulture):null);
    private static QuoteOperationResult<object> Fail(QuoteOperationError e)=>QuoteOperationResult<object>.Failure(e);
}
