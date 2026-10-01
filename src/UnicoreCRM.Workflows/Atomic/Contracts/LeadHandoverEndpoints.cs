using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Workflows.Atomic.Contracts;

public static class LeadHandoverEndpoints
{
    public static IEndpointRouteBuilder MapLeadHandoverEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/leads/{leadId}/handover", HandoverAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("handoverLeadWithTasks");
        return endpoints;
    }
    private static async Task<IResult> HandoverAsync(string leadId, HttpContext context,
        ILeadHandoverWorkflow workflow, CancellationToken ct)
    {
        if (context.Request.Headers["X-Correlation-Id"].ToString().Length is < 8 or > 128)
            return LeadQualificationHttp.Error(new("VALIDATION_FAILED", 422,
                new Dictionary<string, string[]> { ["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."] }), context.TraceIdentifier);
        if (!LeadQualificationHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var body = await LeadQualificationHttp.ReadBodyAsync<LeadHandoverRequest>(context, metadata!.CorrelationId, ct);
        if (body.Error is not null) return body.Error;
        var result = await workflow.ExecuteAsync(new(leadId, body.Value!, metadata.RequestId,
            metadata.CorrelationId, metadata.IdempotencyKey, metadata.ExpectedVersion), ct);
        if (!result.IsSuccess) return LeadQualificationHttp.Error(new(result.ErrorCode!, result.ErrorStatus!.Value,
            result.FieldErrors, result.ExpectedVersion, result.CurrentVersion, result.IdempotencyKey), metadata.CorrelationId);
        context.Response.Headers.ETag = $"\"{result.Response!.Version}\"";
        return Results.Json(result.Response, LeadQualificationHttp.ResponseJson, statusCode: StatusCodes.Status200OK);
    }
}
