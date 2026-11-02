using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Contracts;

internal static class QuoteSalesAcceptanceEndpoints
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/quotes/{quoteId}/sales-acceptances",StaffAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("recordQuoteSalesAcceptance");
        endpoints.MapPost("/quotes/{quoteId}/acceptance-invitations",InviteAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("createQuoteAcceptanceInvitation");
        endpoints.MapGet("/quotes/{quoteId}/acceptance-invitations",HistoryAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("listQuoteAcceptanceInvitations");
        endpoints.MapPost("/public/quote-acceptance/preview",PreviewAsync).AllowAnonymous().RequireRateLimiting("quote-public-consent").WithName("previewCustomerQuoteAcceptance");
        endpoints.MapPost("/public/quote-acceptance/consent",ConsentAsync).AllowAnonymous().RequireRateLimiting("quote-public-consent").WithName("consentCustomerQuoteAcceptance");
    }
    private static async Task<IResult> HistoryAsync(string quoteId,HttpContext context,QuoteSalesAcceptanceService service,CancellationToken ct)
    {
        if(!QuotesHttp.TryMetadata(context,out var metadata,out var error))return error!;
        context.Response.Headers.CacheControl="no-store";
        return QuotesHttp.Result(await service.HistoryAsync(quoteId,metadata!,ct),metadata!.CorrelationId);
    }
    private static async Task<IResult> StaffAsync(string quoteId,HttpContext context,QuoteSalesAcceptanceService service,CancellationToken ct)
    {
        if(!Metadata(context,true,out var command,out var error))return error!;
        try
        {
            var body=await Body<StaffQuoteAcceptanceRequest>(context,ct);
            return QuotesHttp.Result(await service.StaffAsync(quoteId,body,command!,ct),command!.CorrelationId);
        }
        catch(JsonException){return Invalid(context);}
    }
    private static async Task<IResult> InviteAsync(string quoteId,HttpContext context,QuoteSalesAcceptanceService service,CancellationToken ct)
    {
        if(!Metadata(context,true,out var command,out var error))return error!;
        try
        {
            var body=await Body<QuoteInvitationRequest>(context,ct);
            return QuotesHttp.Result(await service.InviteAsync(quoteId,body,command!,ct),command!.CorrelationId);
        }
        catch(JsonException){return Invalid(context);}
    }
    private static async Task<IResult> PreviewAsync(HttpContext context,QuoteSalesAcceptanceService service,CancellationToken ct)
    {
        Secure(context);
        try {var body=await Body<QuoteInvitationPreviewRequest>(context,ct);return QuotesHttp.Result(await service.PreviewAsync(body.Token,ct),context.TraceIdentifier);}
        catch(JsonException){return Invalid(context);}
    }
    private static async Task<IResult> ConsentAsync(HttpContext context,QuoteSalesAcceptanceService service,CancellationToken ct)
    {
        Secure(context);if(!Metadata(context,false,out var command,out var error))return error!;
        try {var body=await Body<CustomerQuoteConsentRequest>(context,ct);return QuotesHttp.Result(await service.ConsentAsync(body,command!,ct),command!.CorrelationId);}
        catch(JsonException){return Invalid(context);}
    }
    private static async Task<T> Body<T>(HttpContext context,CancellationToken ct)
    {
        if(!context.Request.HasJsonContentType() || context.Request.ContentLength>16384)throw new JsonException();
        var bytes=new byte[16385];var count=0;
        while(count<bytes.Length)
        {
            var read=await context.Request.Body.ReadAsync(bytes.AsMemory(count,bytes.Length-count),ct);
            if(read==0)break;count+=read;
        }
        if(count>16384)throw new JsonException();
        using var document=JsonDocument.Parse(bytes.AsMemory(0,count),new(){MaxDepth=12});
        if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();
        var names=document.RootElement.EnumerateObject().Select(x=>x.Name).ToArray();
        if(names.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=names.Length)throw new JsonException();
        return document.RootElement.Deserialize<T>(QuotePricing.Json)??throw new JsonException();
    }
    private static bool Metadata(HttpContext c,bool version,out QuoteCommandMetadata? command,out IResult? error)
    {
        command=null;error=null;if(!QuotesHttp.TryMetadata(c,out var metadata,out error))return false;
        var key=c.Request.Headers["Idempotency-Key"].ToString();
        var match=c.Request.Headers["If-Match"].ToString();long expected=0;
        if(key.Length is <8 or >128 || key.Any(char.IsControl) || version && (match.Length<3 || match[0]!='"' || match[^1]!='"'
            || !long.TryParse(match[1..^1],NumberStyles.None,CultureInfo.InvariantCulture,out expected)))
        {error=Invalid(c);return false;}
        command=new(metadata!.RequestId,metadata.CorrelationId,key,version?expected:null);return true;
    }
    private static IResult Invalid(HttpContext c)=>QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string,string[]>{{"body",["Invalid request or command headers."]}}),c.TraceIdentifier);
    private static void Secure(HttpContext c)
    {c.Response.Headers.CacheControl="no-store";c.Response.Headers["Referrer-Policy"]="no-referrer";c.Response.Headers["X-Content-Type-Options"]="nosniff";}
}
