using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteProofDownload(byte[] bytes,string fileName,string sha256,Action release):IDisposable
{
    private int disposed;
    internal byte[] Bytes=>bytes;internal string FileName=>fileName;internal string Sha256=>sha256;
    public void Dispose(){if(Interlocked.Exchange(ref disposed,1)==0){CryptographicOperations.ZeroMemory(bytes);release();}}
}
internal sealed class QuoteProofScanReads(QuoteProofAccess access,IQuoteProofPersistence proofs,IQuoteProofScanStore store,QuoteProofEffectiveScan effective,IQuotesPersistence quotes,TimeProvider clock)
{
    private static readonly SemaphoreSlim Downloads=new(4,4);
    internal async Task<QuoteOperationResult<QuoteProofPage<QuoteProofScanHistoryItem>>> HistoryAsync(string quoteId,string caseId,string proofId,string? cursor,int limit,QuoteRequestMetadata meta,CancellationToken ct)
    {
        var grant=await access.AuthorizeAsync(quoteId,false,meta,ct);if(!grant.IsSuccess)return QuoteOperationResult<QuoteProofPage<QuoteProofScanHistoryItem>>.Failure(grant.Error!);
        var w=grant.Value!.Quote.WorkspaceId;var row=await proofs.CaseAsync(w,quoteId,caseId,false,ct);
        if(row is null)return QuoteOperationResult<QuoteProofPage<QuoteProofScanHistoryItem>>.Failure(QuoteErrors.NotFound());
        if(await access.StoredAsync(grant.Value,row,meta,ct)is {} denied)return QuoteOperationResult<QuoteProofPage<QuoteProofScanHistoryItem>>.Failure(denied);
        if(await proofs.ProofAsync(w,caseId,proofId,ct)is null)return QuoteOperationResult<QuoteProofPage<QuoteProofScanHistoryItem>>.Failure(QuoteErrors.NotFound());
        var offset=0;if(limit is <1 or >50 || cursor is not null && (!int.TryParse(cursor,NumberStyles.None,CultureInfo.InvariantCulture,out offset)||offset is <0 or >10000))
            return QuoteOperationResult<QuoteProofPage<QuoteProofScanHistoryItem>>.Failure(QuoteErrors.Validation(new Dictionary<string,string[]>{["pagination"]=["Bounded pagination required"]}));
        var attempts=await store.HistoryAsync(w,proofId,offset,limit+1,ct);var page=attempts.Take(limit).ToArray();
        var results=await store.ResultsAsync(w,page.Select(a=>a.AttemptId).ToArray(),ct);
        var items=page.Select(a=>
        {
            var r=results.SingleOrDefault(r=>r.AttemptId==a.AttemptId);string? engine=null,signature=null,updated=null;
            // Explicit allowlist; paths, raw engine output and bytes never enter this DTO.
            if(r is not null)try{using var d=JsonDocument.Parse(r.ProvenanceJson);if(d.RootElement.TryGetProperty("scanner",out var s))
                {engine=s.GetProperty("engineVersion").GetString();signature=s.GetProperty("signatureVersion").GetString();updated=s.GetProperty("signatureUpdatedAt").GetDateTimeOffset().UtcDateTime.ToString("O");}}
                catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){}
            return new QuoteProofScanHistoryItem(a.AttemptId,a.AttemptNumber,r?.Outcome??"SCANNING",r?.ReasonCode??"SCAN_IN_PROGRESS",r?.ParserStatus??"NOT_COMPLETED",r?.MalwareVerdict??"NOT_COMPLETED",
                a.StartedAt.UtcDateTime.ToString("O"),r?.CompletedAt.UtcDateTime.ToString("O"),a.PolicyVersion,a.InputSha256,engine,signature,updated);
        }).ToArray();
        await AuditAsync(w,grant.Value.Grants[0].Trusted.MemberId,proofId,"getQuoteAcceptanceProofScanHistory",meta,ct);
        return QuoteOperationResult<QuoteProofPage<QuoteProofScanHistoryItem>>.Success(new(items,new(attempts.Count>limit,attempts.Count>limit?(offset+limit).ToString(CultureInfo.InvariantCulture):null)));
    }
    internal async Task<QuoteOperationResult<QuoteProofDownload>> DownloadAsync(string quoteId,string caseId,string proofId,QuoteRequestMetadata meta,CancellationToken ct)
    {
        var grant=await access.AuthorizeAsync(quoteId,false,meta,ct,content:true);if(!grant.IsSuccess)return QuoteOperationResult<QuoteProofDownload>.Failure(grant.Error!);
        var w=grant.Value!.Quote.WorkspaceId;var row=await proofs.CaseAsync(w,quoteId,caseId,false,ct);
        if(row is null)return QuoteOperationResult<QuoteProofDownload>.Failure(QuoteErrors.NotFound());
        if(await access.StoredAsync(grant.Value,row,meta,ct)is {} denied)return QuoteOperationResult<QuoteProofDownload>.Failure(denied);
        var proof=await proofs.ProofAsync(w,caseId,proofId,ct);if(proof is null)return QuoteOperationResult<QuoteProofDownload>.Failure(QuoteErrors.NotFound());
        if(!await Downloads.WaitAsync(0,ct))return QuoteOperationResult<QuoteProofDownload>.Failure(new("PROOF_DOWNLOAD_CAPACITY",429,"Proof download capacity unavailable"));
        byte[]? bytes=null;var released=false;
        try
        {
            var state=(await effective.ReadAsync(w,[proof],true,ct))[proof.ProofId];
            if(!state.ContentAvailable)return Blocked(state.ReasonCode);
            bytes=await store.ReadBytesAsync(w,caseId,proofId,ct);
            if(bytes is null || bytes.Length!=proof.FileSize || Convert.ToHexString(SHA256.HashData(bytes))!=proof.Sha256)return Blocked("PROOF_INPUT_BINDING_MISMATCH");
            state=(await effective.ReadAsync(w,[proof],true,ct))[proof.ProofId];if(!state.ContentAvailable)return Blocked(state.ReasonCode);
            await AuditAsync(w,grant.Value.Grants[0].Trusted.MemberId,proofId,"downloadQuoteAcceptanceProofContent",meta,ct);
            var file=new QuoteProofDownload(bytes,proof.FileName,proof.Sha256,()=>Downloads.Release());
            released=true;return QuoteOperationResult<QuoteProofDownload>.Success(file);
        }
        catch(CryptographicException){return Blocked("PROOF_CONTENT_AUTHORITY_UNAVAILABLE");}
        finally{if(!released){if(bytes is not null)CryptographicOperations.ZeroMemory(bytes);Downloads.Release();}}
    }
    private static QuoteOperationResult<QuoteProofDownload> Blocked(string reason)=>QuoteOperationResult<QuoteProofDownload>.Failure(new("PROOF_CONTENT_BLOCKED",409,"Proof content is quarantined",reason));
    private async Task AuditAsync(string w,string actor,string proof,string operation,QuoteRequestMetadata meta,CancellationToken ct)
    {quotes.AddReadAudit(new(operation,w,actor,proof,meta.RequestId,meta.CorrelationId,null,clock.GetUtcNow()));await quotes.SaveChangesAsync(ct);}
}
