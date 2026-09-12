using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Workflows.Atomic.Contracts;

public static class LeadCustomerConversionEndpoints
{
    public static IEndpointRouteBuilder MapLeadCustomerConversionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/workflows/lead-customer-conversion/{leadId}", ConvertAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("convertLeadToCustomer");
        return endpoints;
    }
    private static async Task<IResult> ConvertAsync(string leadId,HttpContext context,ILeadCustomerConversionWorkflow workflow,CancellationToken ct)
    {
        if(!LeadQualificationHttp.TryMetadata(context,out var metadata,out var error))return error!;
        var body=await LeadQualificationHttp.ReadBodyAsync<ConvertLeadToCustomerRequest>(context,metadata!.CorrelationId,ct);
        if(body.Error is not null)return body.Error;
        var result=await workflow.ExecuteAsync(new(leadId,body.Value!,metadata.RequestId,metadata.CorrelationId,metadata.IdempotencyKey,metadata.ExpectedVersion),ct);
        if(!result.IsSuccess)return LeadQualificationHttp.Error(new(result.ErrorCode!,result.ErrorStatus!.Value,result.FieldErrors,result.ExpectedVersion,result.CurrentVersion,result.IdempotencyKey),metadata.CorrelationId);
        context.Response.Headers.ETag=$"\"{result.Response!.Result.LeadVersion}\"";
        return Results.Json(result.Response,LeadQualificationHttp.ResponseJson,statusCode:StatusCodes.Status200OK);
    }
}
