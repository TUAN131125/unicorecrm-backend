using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Deals.Contracts;

public static class DealPipelineEndpoints
{
    public static IEndpointRouteBuilder MapDealPipelineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/crm-configuration/pipelines", ReadAsync).RequireAuthorization().RequireTrustedWorkspace().AddEndpointFilter<DealBusyFilter>().WithName("listCrmPipelines");
        endpoints.MapGet("/crm-configuration/pipelines/{pipelineId}", DetailAsync).RequireAuthorization().RequireTrustedWorkspace().AddEndpointFilter<DealBusyFilter>().WithName("getCrmPipeline");
        endpoints.MapPost("/crm-configuration/pipelines", CreateAsync).RequireAuthorization().RequireTrustedWorkspace().AddEndpointFilter<DealBusyFilter>().WithName("createCrmPipeline");
        endpoints.MapPatch("/crm-configuration/pipelines/{pipelineId}", UpdateAsync).RequireAuthorization().RequireTrustedWorkspace().AddEndpointFilter<DealBusyFilter>().WithName("updateCrmPipeline");
        return endpoints;
    }
    private static async Task<IResult> ReadAsync(HttpContext context, DealPipelineRegistry registry, CancellationToken token)
    {
        if (!DealsHttp.TryMetadata(context, false, false, out var metadata, out var error)) return error!;
        var result = await registry.ReadAsync(new(metadata!.RequestId, metadata.CorrelationId), token);
        return DealsHttp.Result(result, metadata.CorrelationId);
    }
    private static async Task<IResult> DetailAsync(string pipelineId, HttpContext context, DealPipelineRegistry registry, CancellationToken token)
    {
        if (!DealsHttp.TryMetadata(context, false, false, out var metadata, out var error)) return error!;
        var result = await registry.ReadAsync(new(metadata!.RequestId, metadata.CorrelationId), token);
        if (!result.IsSuccess) return DealsHttp.Error(result.Error!, metadata.CorrelationId);
        var value = result.Value!.Items.SingleOrDefault(item => item.Id == pipelineId);
        return value is null ? DealsHttp.Error(DealErrors.NotFound(), metadata.CorrelationId) : Results.Json(value);
    }
    private static Task<IResult> CreateAsync(HttpContext context, DealPipelineRegistry registry, CancellationToken token) => SaveAsync(null, context, registry, token);
    private static Task<IResult> UpdateAsync(string pipelineId, HttpContext context, DealPipelineRegistry registry, CancellationToken token) => SaveAsync(pipelineId, context, registry, token);
    private static async Task<IResult> SaveAsync(string? pipelineId, HttpContext context, DealPipelineRegistry registry, CancellationToken token)
    {
        if (!DealsHttp.TryMetadata(context, true, pipelineId is not null, out var metadata, out var error)) return error!;
        var body = await DealsHttp.ReadBodyAsync<SaveDealPipelineRequest>(context, metadata!.CorrelationId, token);
        if (body.Error is not null) return body.Error;
        var result = await registry.SaveAsync(pipelineId, body.Value!, metadata, token);
        if (result.IsSuccess) context.Response.Headers.ETag = $"\"{result.Value!.Pipeline.ResourceVersion}\"";
        return DealsHttp.Result(result, metadata.CorrelationId, pipelineId is null ? 201 : 200);
    }
}
