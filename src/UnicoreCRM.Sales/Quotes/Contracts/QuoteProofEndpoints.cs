using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Contracts;

internal static class QuoteProofEndpoints
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        var root="/quotes/{quoteId}/acceptance-cases";
        endpoints.MapPost(root,CreateAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("createQuoteAcceptanceCase");
        endpoints.MapPost(root+"/{caseId}/proofs",UploadAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("uploadQuoteAcceptanceProof");
        endpoints.MapGet(root,(string quoteId,HttpContext h,QuoteProofReads r,CancellationToken ct)=>ReadAsync(quoteId,null,null,false,false,h,r,ct)).RequireAuthorization().RequireTrustedWorkspace().WithName("listQuoteAcceptanceCases");
        endpoints.MapGet(root+"/{caseId}",(string quoteId,string caseId,HttpContext h,QuoteProofReads r,CancellationToken ct)=>ReadAsync(quoteId,caseId,null,false,true,h,r,ct)).RequireAuthorization().RequireTrustedWorkspace().WithName("getQuoteAcceptanceCase");
        endpoints.MapGet(root+"/{caseId}/proofs",(string quoteId,string caseId,HttpContext h,QuoteProofReads r,CancellationToken ct)=>ReadAsync(quoteId,caseId,null,false,false,h,r,ct)).RequireAuthorization().RequireTrustedWorkspace().WithName("listQuoteAcceptanceProofs");
        endpoints.MapGet(root+"/{caseId}/proofs/{proofId}",(string quoteId,string caseId,string proofId,HttpContext h,QuoteProofReads r,CancellationToken ct)=>ReadAsync(quoteId,caseId,proofId,false,false,h,r,ct)).RequireAuthorization().RequireTrustedWorkspace().WithName("getQuoteAcceptanceProof");
        endpoints.MapGet(root+"/{caseId}/proofs/{proofId}/security",(string quoteId,string caseId,string proofId,HttpContext h,QuoteProofReads r,CancellationToken ct)=>ReadAsync(quoteId,caseId,proofId,true,false,h,r,ct)).RequireAuthorization().RequireTrustedWorkspace().WithName("getQuoteAcceptanceProofSecurity");
        endpoints.MapGet(root+"/{caseId}/proofs/{proofId}/scans",HistoryAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("getQuoteAcceptanceProofScanHistory");
        endpoints.MapGet(root+"/{caseId}/proofs/{proofId}/content",DownloadAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("downloadQuoteAcceptanceProofContent");
    }
    private static async Task<IResult> HistoryAsync(string quoteId,string caseId,string proofId,HttpContext h,QuoteProofScanReads reads,CancellationToken ct)
    {
        h.Response.Headers.CacheControl="private, no-store";
        if(!QuotesHttp.TryMetadata(h,out var meta,out var error))return error!;
        var raw=h.Request.Query["limit"].FirstOrDefault();var limit=raw is null?20:int.TryParse(raw,out var n)?n:0;
        var result=await reads.HistoryAsync(quoteId,caseId,proofId,h.Request.Query["cursor"].FirstOrDefault(),limit,new(meta!.RequestId,meta.CorrelationId),ct);
        return result.IsSuccess?Results.Json(result.Value):QuotesHttp.Error(result.Error!,meta.CorrelationId);
    }
    private static async Task<IResult> DownloadAsync(string quoteId,string caseId,string proofId,HttpContext h,QuoteProofScanReads reads,CancellationToken ct)
    {
        h.Response.Headers.CacheControl="private, no-store";
        if(!QuotesHttp.TryMetadata(h,out var meta,out var error))return error!;
        var result=await reads.DownloadAsync(quoteId,caseId,proofId,new(meta!.RequestId,meta.CorrelationId),ct);
        if(!result.IsSuccess)return QuotesHttp.Error(result.Error!,meta.CorrelationId);
        return new ProofDownloadResult(result.Value!);
    }
    private sealed class ProofDownloadResult(QuoteProofDownload file):IResult
    {
        public async Task ExecuteAsync(HttpContext h)
        {
            using(file)
            {
                h.Response.Headers["X-Content-SHA256"]=file.Sha256;h.Response.Headers["X-Content-Type-Options"]="nosniff";
                h.Response.Headers.CacheControl="private, no-store";h.Response.Headers.ContentSecurityPolicy="sandbox; default-src 'none'; frame-ancestors 'none'";h.Response.Headers["X-Frame-Options"]="DENY";
                await Results.File(file.Bytes,"application/pdf",file.FileName,enableRangeProcessing:false).ExecuteAsync(h);
            }
        }
    }
    private static async Task<IResult> ReadAsync(string quoteId,string? caseId,string? proofId,bool security,bool single,HttpContext h,QuoteProofReads reads,CancellationToken ct)
    {
        if(!QuotesHttp.TryMetadata(h,out var meta,out var error))return error!;
        h.Response.Headers.CacheControl="private, no-store";
        var parsed=h.Request.Query["limit"].FirstOrDefault();var limit=parsed is null?20:int.TryParse(parsed,out var number)?number:0;
        var result=await reads.ReadAsync(quoteId,caseId,proofId,security,single,h.Request.Query["cursor"].FirstOrDefault(),limit,new(meta!.RequestId,meta.CorrelationId),ct);
        return result.IsSuccess?Results.Json(result.Value):QuotesHttp.Error(result.Error!,meta.CorrelationId);
    }
    private static async Task<IResult> CreateAsync(string quoteId,HttpContext h,QuoteProofCommands commands,QuoteProofAccess access,CancellationToken ct)
    {
        if(!Metadata(h,out var meta,out var error))return error!;
        var allowed=await access.AuthorizeAsync(quoteId,true,new(meta!.RequestId,meta.CorrelationId),ct);
        if(!allowed.IsSuccess)return QuotesHttp.Error(allowed.Error!,meta.CorrelationId);
        CreateQuoteAcceptanceCaseRequest? request;
        try
        {
            if(!h.Request.HasJsonContentType() || h.Request.ContentLength>8192 || h.Request.Query.Count>0)throw new JsonException();
            using var buffer=new MemoryStream();var part=new byte[1024];int count;
            while((count=await h.Request.Body.ReadAsync(part,ct))>0){if(buffer.Length+count>8192)throw new JsonException();await buffer.WriteAsync(part.AsMemory(0,count),ct);}
            request=JsonSerializer.Deserialize<CreateQuoteAcceptanceCaseRequest>(buffer.ToArray(),QuotePricing.Json);if(request is null)throw new JsonException();
        }
        catch(JsonException){return QuotesHttp.Error(new("PROOF_INPUT_INVALID",422,"Only typed case binding is admitted"),meta.CorrelationId);}
        return await MutationAsync(h,meta,()=>commands.CreateAsync(quoteId,request,meta,ct));
    }
    private static async Task<IResult> UploadAsync(string quoteId,string caseId,HttpContext h,QuoteProofCommands commands,CancellationToken ct)
    {
        if(!Metadata(h,out var meta,out var error))return error!;
        // No client trust/identity/storage fields are admitted, in either query or custom headers.
        var forbidden=new[]{"scanstatus","verificationstatus","istrusted","isclean","verifiedby","serversha256","storagereference","acceptancestatus","uploadedby","workspaceid"};
        if(h.Request.Query.Count>0 || h.Request.Headers.Keys.Any(k=>k.StartsWith("X-Proof-",StringComparison.OrdinalIgnoreCase) && !new[]{"X-Proof-File-Name","X-Proof-Evidence-Type"}.Contains(k,StringComparer.OrdinalIgnoreCase)) || h.Request.Headers.Keys.Any(k=>forbidden.Any(f=>k.Replace("-","").Equals(f,StringComparison.OrdinalIgnoreCase)||k.Replace("-","").Equals("xproof"+f,StringComparison.OrdinalIgnoreCase) || f!="workspaceid" && k.Replace("-","").Equals("x"+f,StringComparison.OrdinalIgnoreCase))))
            return QuotesHttp.Error(new("PROOF_INPUT_INVALID",422,"Client trust and storage claims are not admitted"),meta!.CorrelationId);
        var size=h.Features.Get<IHttpMaxRequestBodySizeFeature>();if(size is {IsReadOnly:false})size.MaxRequestBodySize=QuoteProofInput.MaximumBytes+1;
        return await MutationAsync(h,meta!,()=>commands.UploadAsync(quoteId,caseId,h.Request.Body,h.Request.ContentLength,h.Request.ContentType,
            h.Request.Headers["X-Proof-File-Name"].ToString(),h.Request.Headers["X-Proof-Evidence-Type"].ToString(),meta!,ct));
    }
    private static bool Metadata(HttpContext h,out QuoteCommandMetadata? command,out IResult? error)
    {
        command=null;if(!QuotesHttp.TryMetadata(h,out var meta,out error))return false;
        h.Response.Headers.CacheControl="private, no-store";
        var key=h.Request.Headers["Idempotency-Key"].ToString();var match=h.Request.Headers.IfMatch.ToString();
        if(key.Length is <8 or >128 || key.Any(char.IsControl) || match.Length<3 || match[0]!='"' || match[^1]!='"'
            || !long.TryParse(match[1..^1],NumberStyles.None,CultureInfo.InvariantCulture,out var version) || version<1)
        {error=QuotesHttp.Error(new("PROOF_INPUT_INVALID",422,"Strong If-Match and bounded idempotency key required"),meta!.CorrelationId);return false;}
        command=new(meta!.RequestId,meta.CorrelationId,key,version);return true;
    }
    private static async Task<IResult> MutationAsync(HttpContext h,QuoteCommandMetadata meta,Func<Task<QuoteOperationResult<QuoteProofMutation>>> action)
    {
        try
        {
            var result=await action();if(!result.IsSuccess)return QuotesHttp.Error(result.Error!,meta.CorrelationId);
            h.Response.Headers.ETag="\""+result.Value!.Result.Case.ResourceVersion.ToString(CultureInfo.InvariantCulture)+"\"";
            return Results.Json(result.Value,statusCode:201);
        }
        catch(Microsoft.Data.SqlClient.SqlException e)when(e.Number is 1205 or 1222 or 51001)
        {return QuotesHttp.Error(new("IDEMPOTENCY_REQUEST_IN_PROGRESS",409,"Proof storage is contended"),meta.CorrelationId);}
        catch(QuotePersistenceConcurrencyException){return QuotesHttp.Error(new("VERSION_CONFLICT",412,"Resource version conflict"),meta.CorrelationId);}
    }
}

