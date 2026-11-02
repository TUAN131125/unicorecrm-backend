using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Contracts;

internal static class QuoteIdentityEndpoints
{
    internal static void Map(IEndpointRouteBuilder routes)
    {
        const string root="/quotes/{quoteId}/acceptance-cases/{caseId}/identity-authority";
        routes.MapGet(root,ReadAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("getQuoteIdentityAuthority");
        routes.MapPut(root+"/binding",SaveAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("saveQuoteIdentityAuthorityBinding");
        routes.MapPost(root+"/source-checks",(string quoteId,string caseId,HttpContext h,QuoteIdentityCaseService service,CancellationToken ct)=>ActAsync(quoteId,caseId,true,h,service,ct)).RequireAuthorization().RequireTrustedWorkspace().WithName("recordQuoteAcceptanceSourceCheck");
        routes.MapPost(root+"/assessments",(string quoteId,string caseId,HttpContext h,QuoteIdentityCaseService service,CancellationToken ct)=>ActAsync(quoteId,caseId,false,h,service,ct)).RequireAuthorization().RequireTrustedWorkspace().WithName("assessQuoteIdentityAuthority");
        routes.MapGet(root+"/history",HistoryAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("listQuoteIdentityAuthorityHistory");
        const string review="/quotes/{quoteId}/acceptance-cases/{caseId}/independent-review";
        routes.MapGet(review,ReviewReadAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("getQuoteIndependentReview");
        routes.MapGet(review+"/history",ReviewHistoryAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("listQuoteIndependentReviewHistory");
        routes.MapPost(review+"/decisions",ReviewSubmitAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("submitQuoteIndependentReviewDecision");
        const string sources="/quotes/{quoteId}/acceptance-evidence-sources";
        routes.MapGet(sources,(string quoteId,HttpContext h,QuoteEvidenceSourceService service,CancellationToken ct)=>SourcesAsync(quoteId,null,h,service,ct)).RequireAuthorization().RequireTrustedWorkspace().WithName("listQuoteAcceptanceEvidenceSources");
        routes.MapGet(sources+"/{sourceId}/history",(string quoteId,string sourceId,HttpContext h,QuoteEvidenceSourceService service,CancellationToken ct)=>SourcesAsync(quoteId,sourceId,h,service,ct)).RequireAuthorization().RequireTrustedWorkspace().WithName("listQuoteAcceptanceEvidenceSourceHistory");
        routes.MapPost(sources,(string quoteId,HttpContext h,QuoteEvidenceSourceService service,CancellationToken ct)=>SourceWriteAsync(quoteId,null,"CREATE",h,service,ct)).RequireAuthorization().RequireTrustedWorkspace().WithName("registerQuoteAcceptanceEvidenceSource");
        routes.MapPut(sources+"/{sourceId}",(string quoteId,string sourceId,HttpContext h,QuoteEvidenceSourceService service,CancellationToken ct)=>SourceWriteAsync(quoteId,sourceId,"REVISE",h,service,ct)).RequireAuthorization().RequireTrustedWorkspace().WithName("reviseQuoteAcceptanceEvidenceSource");
        routes.MapPost(sources+"/{sourceId}/revoke",(string quoteId,string sourceId,HttpContext h,QuoteEvidenceSourceService service,CancellationToken ct)=>SourceWriteAsync(quoteId,sourceId,"REVOKE",h,service,ct)).RequireAuthorization().RequireTrustedWorkspace().WithName("revokeQuoteAcceptanceEvidenceSource");
        routes.MapPost(sources+"/{sourceId}/approve",(string quoteId,string sourceId,HttpContext h,QuoteEvidenceSourceService service,CancellationToken ct)=>SourceWriteAsync(quoteId,sourceId,"APPROVE",h,service,ct)).RequireAuthorization().RequireTrustedWorkspace().WithName("approveQuoteAcceptanceEvidenceSource");
    }
    private static async Task<IResult> ReviewReadAsync(string quoteId,string caseId,HttpContext h,QuoteIndependentReviewService service,CancellationToken ct)
    {if(!ReadMeta(h,out var meta,out var error))return error!;return Result(await service.ReadAsync(quoteId,caseId,meta!,ct),meta!.CorrelationId);}
    private static async Task<IResult> ReviewHistoryAsync(string quoteId,string caseId,HttpContext h,QuoteIndependentReviewService service,CancellationToken ct)
    {if(!ReadMeta(h,out var meta,out var error))return error!;Page(h,out var offset,out var limit);return Result(await service.HistoryAsync(quoteId,caseId,offset,limit,meta!,ct),meta!.CorrelationId);}
    private static async Task<IResult> ReviewSubmitAsync(string quoteId,string caseId,HttpContext h,QuoteIndependentReviewService service,CancellationToken ct)
    {
        if(!WriteMeta(h,out var command,out var error))return error!;var body=await BodyAsync<SubmitQuoteIndependentReview>(h,ct);
        if(body is null)return QuotesHttp.Error(new("INDEPENDENT_REVIEW_INPUT_INVALID",422,"Invalid independent review input"),command!.CorrelationId);
        return await MutateAsync(h,command!,()=>service.SubmitAsync(quoteId,caseId,body,command!,ct));
    }
    private static async Task<IResult> ReadAsync(string quoteId,string caseId,HttpContext h,QuoteIdentityCaseService service,CancellationToken ct)
    {if(!ReadMeta(h,out var meta,out var error))return error!;return Result(await service.ReadAsync(quoteId,caseId,meta!,ct),meta!.CorrelationId);}
    private static async Task<IResult> HistoryAsync(string quoteId,string caseId,HttpContext h,QuoteIdentityCaseService service,CancellationToken ct)
    {if(!ReadMeta(h,out var meta,out var error))return error!;Page(h,out var offset,out var limit);return Result(await service.HistoryAsync(quoteId,caseId,offset,limit,meta!,ct),meta!.CorrelationId);}
    private static async Task<IResult> SourcesAsync(string quoteId,string? sourceId,HttpContext h,QuoteEvidenceSourceService service,CancellationToken ct)
    {if(!ReadMeta(h,out var meta,out var error))return error!;Page(h,out var offset,out var limit);return Result(await service.ReadAsync(quoteId,sourceId,offset,limit,meta!,ct),meta!.CorrelationId);}
    private static async Task<IResult> SaveAsync(string quoteId,string caseId,HttpContext h,QuoteIdentityCaseService service,CancellationToken ct)
    {
        if(!WriteMeta(h,out var command,out var error))return error!;var body=await BodyAsync<SaveQuoteIdentityBindingRequest>(h,ct);
        if(body is null)return QuotesHttp.Error(QuoteIdentityAccess.Input(),command!.CorrelationId);
        return await MutateAsync(h,command!,()=>service.SaveAsync(quoteId,caseId,body,command!,ct));
    }
    private static async Task<IResult> ActAsync(string quoteId,string caseId,bool sourceCheck,HttpContext h,QuoteIdentityCaseService service,CancellationToken ct)
    {
        if(!WriteMeta(h,out var command,out var error))return error!;var body=await BodyAsync<QuoteIdentityActionRequest>(h,ct);
        if(body is null)return QuotesHttp.Error(QuoteIdentityAccess.Input(),command!.CorrelationId);
        return await MutateAsync(h,command!,()=>service.ActAsync(quoteId,caseId,body,sourceCheck,command!,ct));
    }
    private static async Task<IResult> SourceWriteAsync(string quoteId,string? sourceId,string kind,HttpContext h,QuoteEvidenceSourceService service,CancellationToken ct)
    {
        if(!WriteMeta(h,out var command,out var error))return error!;
        var request=kind is "CREATE" or "REVISE"?await BodyAsync<QuoteEvidenceSourceRequest>(h,ct):null;
        var action=kind is "REVOKE" or "APPROVE"?await BodyAsync<QuoteEvidenceSourceAction>(h,ct):null;
        if(request is null && action is null)return QuotesHttp.Error(QuoteIdentityAccess.Input(),command!.CorrelationId);
        return await MutateAsync(h,command!,()=>service.WriteAsync(quoteId,sourceId,request,action,kind,command!,ct));
    }
    private static async Task<T?> BodyAsync<T>(HttpContext h,CancellationToken ct)
    {
        if(!h.Request.HasJsonContentType() || h.Request.ContentLength>16384 || h.Request.Query.Count>0)return default;
        try
        {
            using var data=new MemoryStream();var buffer=new byte[2048];int count;
            while((count=await h.Request.Body.ReadAsync(buffer,ct))>0){if(data.Length+count>16384)return default;await data.WriteAsync(buffer.AsMemory(0,count),ct);}
            return JsonSerializer.Deserialize<T>(data.ToArray(),QuotePricing.Json);
        }
        catch(JsonException){return default;}
    }
    private static bool ReadMeta(HttpContext h,out QuoteRequestMetadata? meta,out IResult? error)
    {h.Response.Headers.CacheControl="private, no-store";meta=null;if(!QuotesHttp.TryMetadata(h,out var m,out error))return false;meta=new(m!.RequestId,m.CorrelationId);return true;}
    private static bool WriteMeta(HttpContext h,out QuoteCommandMetadata? command,out IResult? error)
    {
        command=null;if(!ReadMeta(h,out var meta,out error))return false;var key=h.Request.Headers["Idempotency-Key"].ToString();var match=h.Request.Headers.IfMatch.ToString();
        if(key.Length is <8 or >128 || key.Any(char.IsControl) || match.Length<3 || match[0]!='"' || match[^1]!='"' || !long.TryParse(match[1..^1],NumberStyles.None,CultureInfo.InvariantCulture,out var version) || version<0)
        {error=QuotesHttp.Error(QuoteIdentityAccess.Input(),meta!.CorrelationId);return false;}
        command=new(meta!.RequestId,meta.CorrelationId,key,version);return true;
    }
    private static void Page(HttpContext h,out int offset,out int limit)
    {var c=h.Request.Query["cursor"].FirstOrDefault();offset=c is null?0:int.TryParse(c,NumberStyles.None,CultureInfo.InvariantCulture,out var n)?n:-1;var s=h.Request.Query["limit"].FirstOrDefault();limit=s is null?20:int.TryParse(s,out var l)?l:0;}
    private static async Task<IResult> MutateAsync<T>(HttpContext h,QuoteCommandMetadata command,Func<Task<QuoteOperationResult<QuoteIdentityMutation<T>>>> action)
    {
        try{var result=await action();if(result.IsSuccess)h.Response.Headers.ETag="\""+result.Value!.Version.ToString(CultureInfo.InvariantCulture)+"\"";return Result(result,command.CorrelationId);}
        catch(Microsoft.Data.SqlClient.SqlException e)when(e.Number is 1205 or 1222 or 51001){return QuotesHttp.Error(new("IDEMPOTENCY_REQUEST_IN_PROGRESS",409,"Evidence mutation is contended"),command.CorrelationId);}
        catch(QuotePersistenceConcurrencyException){return QuotesHttp.Error(QuoteIdentityAccess.Stale(),command.CorrelationId);}
    }
    private static IResult Result<T>(QuoteOperationResult<T> result,string correlation)=>result.IsSuccess?Results.Json(result.Value):QuotesHttp.Error(result.Error!,correlation);
}
