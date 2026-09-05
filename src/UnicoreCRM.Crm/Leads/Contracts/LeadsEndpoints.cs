using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Contracts;

public static class LeadsEndpoints
{
    public static IEndpointRouteBuilder MapLeadsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapGet(endpoints, "/leads", ListLeadsAsync, "listLeads");
        MapPost(endpoints, "/leads", CreateLeadAsync, "createLead");
        MapGet(endpoints, "/leads/{leadId}", GetLeadAsync, "getLead");
        MapPut(endpoints, "/leads/{leadId}", ReplaceLeadProfileAsync, "replaceLeadProfile");
        MapPost(endpoints, "/leads/{leadId}/advance-work-state", AdvanceLeadWorkStateAsync, "advanceLeadWorkState");
        MapPost(endpoints, "/leads/{leadId}/disqualify", DisqualifyLeadAsync, "disqualifyLead");
        MapPost(endpoints, "/leads/{leadId}/reopen", ReopenDisqualifiedLeadAsync, "reopenDisqualifiedLead");
        return endpoints;
    }

    private static void MapGet(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapGet(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static void MapPost(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapPost(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static void MapPut(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapPut(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static async Task<IResult> ListLeadsAsync(
        string? cursor,
        int? limit,
        string? search,
        string? workState,
        string? ownerId,
        HttpContext context,
        Application.ListLeads.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!LeadsHttp.TryMetadata(context, false, false, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(
            new Application.ListLeads.Query(
                cursor, limit, search, workState, ownerId, metadata!.RequestId, metadata.CorrelationId),
            cancellationToken);
        if (!result.IsSuccess)
            return LeadsHttp.Error(result.Error!, metadata.CorrelationId);
        if (result.Value!.NextCursor is { } nextCursor)
            context.Response.Headers["X-Next-Cursor"] = nextCursor;
        return Results.Json(result.Value.Items);
    }

    private static async Task<IResult> CreateLeadAsync(
        HttpContext context,
        Application.CreateLead.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!LeadsHttp.TryMetadata(context, true, false, out var metadata, out var error))
            return error!;
        var body = await LeadsHttp.ReadBodyAsync<CreateLeadRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await handler.HandleAsync(new Application.CreateLead.Command(body.Value!, metadata), cancellationToken);
        return LeadsHttp.Result(result, metadata.CorrelationId, StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetLeadAsync(
        string leadId,
        HttpContext context,
        Application.GetLead.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!LeadsHttp.TryMetadata(context, false, false, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(
            new Application.GetLead.Query(leadId, metadata!.RequestId, metadata.CorrelationId),
            cancellationToken);
        return LeadsHttp.Result(result, metadata.CorrelationId);
    }

    private static Task<IResult> ReplaceLeadProfileAsync(
        string leadId,
        HttpContext context,
        Application.ReplaceLeadProfile.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync<ReplaceLeadProfileRequest>(leadId, context, cancellationToken,
            (request, metadata) => handler.HandleAsync(new(leadId, request, metadata), cancellationToken));

    private static Task<IResult> AdvanceLeadWorkStateAsync(
        string leadId,
        HttpContext context,
        Application.AdvanceLeadWorkState.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync<AdvanceLeadWorkStateRequest>(leadId, context, cancellationToken,
            (request, metadata) => handler.HandleAsync(new(leadId, request, metadata), cancellationToken));

    private static Task<IResult> DisqualifyLeadAsync(
        string leadId,
        HttpContext context,
        Application.DisqualifyLead.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync<DisqualifyLeadRequest>(leadId, context, cancellationToken,
            (request, metadata) => handler.HandleAsync(new(leadId, request, metadata), cancellationToken));

    private static Task<IResult> ReopenDisqualifiedLeadAsync(
        string leadId,
        HttpContext context,
        Application.ReopenDisqualifiedLead.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync<ReopenDisqualifiedLeadRequest>(leadId, context, cancellationToken,
            (request, metadata) => handler.HandleAsync(new(leadId, request, metadata), cancellationToken));

    private static async Task<IResult> ExecuteCommandAsync<TRequest>(
        string leadId,
        HttpContext context,
        CancellationToken cancellationToken,
        Func<TRequest, LeadCommandMetadata, Task<LeadOperationResult<LeadMutationResponse>>> execute)
        where TRequest : class
    {
        if (!LeadsHttp.TryMetadata(context, true, true, out var metadata, out var error))
            return error!;
        var body = await LeadsHttp.ReadBodyAsync<TRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await execute(body.Value!, metadata);
        return LeadsHttp.Result(result, metadata.CorrelationId);
    }
}

internal static class LeadsHttp
{
    internal static bool TryMetadata(
        HttpContext context,
        bool requireIdempotency,
        bool requireIfMatch,
        out LeadCommandMetadata? metadata,
        out IResult? error)
    {
        metadata = null;
        error = null;
        var requestId = context.Request.Headers["X-Request-Id"].ToString();
        var suppliedCorrelation = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelation.Length is >= 8 and <= 128 ? suppliedCorrelation : context.TraceIdentifier;
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        var ifMatch = context.Request.Headers.IfMatch.ToString();
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (requestId.Length is < 8 or > 128)
            fields["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."];
        if (suppliedCorrelation.Length != 0 && suppliedCorrelation.Length is < 8 or > 128)
            fields["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."];
        if (requireIdempotency && idempotencyKey.Length is < 8 or > 128)
            fields["Idempotency-Key"] = ["Idempotency-Key must contain between 8 and 128 characters."];
        long? expectedVersion = null;
        if (requireIfMatch && !TryExpectedVersion(ifMatch, out expectedVersion))
            fields["If-Match"] = ["If-Match must contain a quoted non-negative resource version."];
        if (fields.Count != 0)
        {
            error = Error(LeadErrors.Validation(fields), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new LeadCommandMetadata(requestId, correlationId, idempotencyKey, expectedVersion);
        return true;
    }

    internal static async Task<BodyRead<T>> ReadBodyAsync<T>(
        HttpContext context,
        string correlationId,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(cancellationToken);
            return value is null
                ? new(null, BodyError("A JSON request body is required.", correlationId))
                : new(value, null);
        }
        catch (JsonException)
        {
            return new(null, BodyError("The JSON request body does not match the contract.", correlationId));
        }
        catch (NotSupportedException)
        {
            return new(null, BodyError("A JSON request body is required.", correlationId));
        }
    }

    internal static IResult Result<T>(
        LeadOperationResult<T> result,
        string correlationId,
        int successStatus = StatusCodes.Status200OK) =>
        result.IsSuccess ? Results.Json(result.Value, statusCode: successStatus) : Error(result.Error!, correlationId);

    internal static IResult Error(LeadOperationError error, string correlationId) =>
        Results.Json(
            new LeadProblemDetails(
                $"urn:unicore:error:{error.Code.ToLowerInvariant()}",
                error.Title,
                error.Status,
                error.Code,
                false,
                correlationId,
                error.Detail,
                null,
                error.FieldErrors,
                error.AggregateId,
                error.ExpectedVersion,
                error.CurrentVersion,
                error.IdempotencyKey),
            statusCode: error.Status,
            contentType: "application/problem+json");

    private static bool TryExpectedVersion(string supplied, out long? expectedVersion)
    {
        expectedVersion = null;
        var value = supplied.StartsWith("W/", StringComparison.Ordinal) ? supplied[2..] : supplied;
        if (value.Length < 3 || value[0] != '"' || value[^1] != '"')
            return false;
        if (!long.TryParse(value[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            return false;
        expectedVersion = parsed;
        return true;
    }

    private static IResult BodyError(string message, string correlationId) =>
        Error(LeadErrors.Validation(new Dictionary<string, string[]> { ["body"] = [message] }), correlationId);

    internal sealed record BodyRead<T>(T? Value, IResult? Error) where T : class;
}
