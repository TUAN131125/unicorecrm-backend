using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.BuildingBlocks;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace UnicoreCRM.Workflows.Atomic.Contracts;
public static class OrderConfirmationEndpoints
{
    public static IEndpointRouteBuilder MapOrderConfirmationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/workflows/order-confirmation/{orderId}/confirm-with-payment-plan",ConfirmAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("confirmOrderWithPaymentPlan");return endpoints;
    }
    private static async Task<IResult> ConfirmAsync(string orderId,HttpContext context,Application.ConfirmOrder.Handler handler,CancellationToken ct)
    {
        if(!LeadQualificationHttp.TryMetadata(context,out var meta,out var error))return error!;
        try
        {
            if(!context.Request.HasJsonContentType() || context.Request.ContentLength>16384)throw new JsonException();
            var bytes=new byte[16385];var count=0;
            while(count<bytes.Length){var n=await context.Request.Body.ReadAsync(bytes.AsMemory(count),ct);if(n==0)break;count+=n;}
            if(count>16384)throw new JsonException();
            using var doc=JsonDocument.Parse(bytes.AsMemory(0,count),new(){MaxDepth=12});
            if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();
            var names=doc.RootElement.EnumerateObject().Select(x=>x.Name).ToArray();
            if(names.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=names.Length)throw new JsonException();
            var body=doc.RootElement.Deserialize<ConfirmOrderWithPaymentPlanRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web){UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow})??throw new JsonException();
            var response=await handler.ExecuteAsync(orderId,body,meta!.RequestId,meta.CorrelationId,meta.IdempotencyKey,meta.ExpectedVersion,ct);
            context.Response.Headers.ETag=$"\"{response.Version}\"";context.Response.Headers.CacheControl="no-store";
            return Results.Json(response,LeadQualificationHttp.ResponseJson);
        }
        catch(JsonException){return LeadQualificationHttp.Error(new("VALIDATION_FAILED",422),meta!.CorrelationId);}
        catch(BusinessCommandException e){return LeadQualificationHttp.Error(new(e.Code,e.Status,new Dictionary<string,string[]>{{"confirmation",[e.Code]}}),meta!.CorrelationId);}
    }
}
