using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Organizations.Contracts;

public static class OrganizationsEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/organizations", ListOrganizationsAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("listOrganizations");
        endpoints.MapGet("/organizations/{organizationId}", GetOrganizationAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getOrganization");
        endpoints.MapGet("/organizations/{organizationId}/overview", GetOrganizationOverviewAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("getOrganizationOverview");
        endpoints.MapPost("/organizations", CreateOrganizationAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("createOrganization");
        endpoints.MapPatch("/organizations/{organizationId}", UpdateOrganizationAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("updateOrganization");
        endpoints.MapPost("/organizations/{organizationId}/archive", ArchiveOrganizationAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("archiveOrganization");
        return endpoints;
    }

    private static async Task<IResult> ListOrganizationsAsync(
        HttpContext context,
        Application.ListOrganizations.Handler handler,
        CancellationToken cancellationToken,
        string? q = null,string? status = null,string? industry = null,string? sizeBand = null,string? ownerId = null,string? cursor = null,int limit = 50)
    {
        if (!OrganizationsHttp.TryMetadata(context, out var metadata, out var error))
            return error!;
        if(limit is <1 or >250)return OrganizationsHttp.ErrorResult(OrganizationErrors.Validation(new Dictionary<string,string[]>{{"limit",["limit must be between 1 and 250."]}}),metadata!.CorrelationId);
        var result = await handler.HandleAsync(new(metadata!,q,status,industry,sizeBand,ownerId,cursor,limit), cancellationToken);
        return OrganizationsHttp.Result(result, metadata!.CorrelationId);
    }

    private static async Task<IResult> GetOrganizationAsync(
        string organizationId,
        HttpContext context,
        Application.GetOrganization.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!OrganizationsHttp.TryMetadata(context, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(new(organizationId, metadata!), cancellationToken);
        return OrganizationsHttp.Result(result, metadata!.CorrelationId);
    }
    private static async Task<IResult> GetOrganizationOverviewAsync(string organizationId,HttpContext context,Application.GetOrganizationOverview.Handler handler,CancellationToken ct)
    {if(!OrganizationsHttp.TryMetadata(context,out var metadata,out var error))return error!;return OrganizationsHttp.Result(await handler.HandleAsync(new(organizationId,metadata!),ct),metadata!.CorrelationId);}

    private static async Task<IResult> CreateOrganizationAsync(HttpContext context, Application.CreateOrganization.Handler handler, CancellationToken ct)
    {
        if (!OrganizationsHttp.TryCommandMetadata(context, false, out var metadata, out var error)) return error!;
        var body=await OrganizationsHttp.ReadBodyAsync<CreateOrganizationRequest>(context,metadata!.CorrelationId,ct); if(body.Error is not null)return body.Error;
        return OrganizationsHttp.Result(await handler.HandleAsync(new(body.Value!,metadata),ct),metadata.CorrelationId);
    }
    private static async Task<IResult> UpdateOrganizationAsync(string organizationId,HttpContext context,Application.UpdateOrganization.Handler handler,CancellationToken ct)
    {
        if(!OrganizationsHttp.TryCommandMetadata(context,true,out var metadata,out var error))return error!;var body=await OrganizationsHttp.ReadBodyAsync<UpdateOrganizationRequest>(context,metadata!.CorrelationId,ct);if(body.Error is not null)return body.Error;return OrganizationsHttp.Result(await handler.HandleAsync(new(organizationId,body.Value!,metadata),ct),metadata.CorrelationId);
    }
    private static async Task<IResult> ArchiveOrganizationAsync(string organizationId,HttpContext context,Application.ArchiveOrganization.Handler handler,CancellationToken ct)
    {
        if(!OrganizationsHttp.TryCommandMetadata(context,true,out var metadata,out var error))return error!;var body=await OrganizationsHttp.ReadBodyAsync<ArchiveOrganizationRequest>(context,metadata!.CorrelationId,ct);if(body.Error is not null)return body.Error;return OrganizationsHttp.Result(await handler.HandleAsync(new(organizationId,metadata!),ct),metadata.CorrelationId);
    }
}

internal static class OrganizationsHttp
{
    internal static bool TryMetadata(
        HttpContext context,
        out OrganizationRequestMetadata? metadata,
        out IResult? error)
    {
        metadata = null;
        error = null;
        var requestId = context.Request.Headers["X-Request-Id"].ToString();
        var suppliedCorrelation = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelation.Length is >= 8 and <= 128
            ? suppliedCorrelation
            : context.TraceIdentifier;
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (requestId.Length is < 8 or > 128)
            fields["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."];
        if (suppliedCorrelation.Length != 0 && suppliedCorrelation.Length is < 8 or > 128)
            fields["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."];
        if (fields.Count != 0)
        {
            error = Error(OrganizationErrors.Validation(fields, StatusCodes.Status400BadRequest), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new OrganizationRequestMetadata(requestId, correlationId);
        return true;
    }

    internal static IResult Result<T>(OrganizationOperationResult<T> result, string correlationId) =>
        result.IsSuccess ? Results.Json(result.Value) : Error(result.Error!, correlationId);
    internal static IResult ErrorResult(OrganizationOperationError error,string correlationId)=>Error(error,correlationId);

    internal static bool TryCommandMetadata(HttpContext context,bool requireIfMatch,out OrganizationCommandMetadata? metadata,out IResult? error)
    {
        metadata=null;if(!TryMetadata(context,out var read,out error))return false;var key=context.Request.Headers["Idempotency-Key"].ToString();var match=context.Request.Headers.IfMatch.ToString();var fields=new Dictionary<string,string[]>();if(key.Length is <8 or >128)fields["Idempotency-Key"]=["Idempotency-Key must contain between 8 and 128 characters."];long? expected=null;if(requireIfMatch&&!TryExpectedVersion(match,out expected))fields["If-Match"]=["If-Match must contain a quoted non-negative resource version."];if(fields.Count>0){error=Error(OrganizationErrors.Validation(fields,400),read!.CorrelationId);return false;}metadata=new(read!.RequestId,read.CorrelationId,key,expected);return true;
    }
    internal static async Task<OrganizationBodyRead<T>> ReadBodyAsync<T>(HttpContext context,string correlationId,CancellationToken ct) where T:class
    {try{var value=await context.Request.ReadFromJsonAsync<T>(cancellationToken:ct);return value is null?new(null,Error(OrganizationErrors.Validation(new Dictionary<string,string[]>{{"body",["A JSON request body is required."]}},400),correlationId)):new(value,null);}catch(JsonException){return new(null,Error(OrganizationErrors.Validation(new Dictionary<string,string[]>{{"body",["The JSON request body is invalid."]}},400),correlationId));}}
    private static bool TryExpectedVersion(string value,out long? version){version=null;if(value.Length<3||value[0]!='"'||value[^1]!='"')return false;if(!long.TryParse(value[1..^1],out var parsed)||parsed<0)return false;version=parsed;return true;}

    private static IResult Error(OrganizationOperationError error, string correlationId) =>
        Results.Json(
            new OrganizationProblemDetails(
                $"urn:unicore:error:{error.Code.ToLowerInvariant()}",
                error.Title,
                error.Status,
                error.Code,
                false,
                correlationId,
                error.Detail,
                error.FieldErrors),
            statusCode: error.Status,
            contentType: "application/problem+json");
}

internal sealed record OrganizationBodyRead<T>(T? Value,IResult? Error);

internal sealed record OrganizationProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? FieldErrors = null);
