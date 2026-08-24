using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Deals.Contracts;

public static class DealsEndpoints
{
    public static IEndpointRouteBuilder MapDealsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapGet(endpoints, "/deals", ListDealsAsync, "listDeals");
        MapPost(endpoints, "/deals", CreateDealAsync, "createDealCommand");
        MapGet(endpoints, "/deals/forecast-summary", GetForecastSummaryAsync, "getDealForecastSummary");
        MapPost(endpoints, "/deals/archive-batch", ArchiveBatchAsync, "archiveDealsBatch");
        MapGet(endpoints, "/deals/{dealId}", GetDealAsync, "getDeal");
        MapPost(endpoints, "/deals/{dealId}/archive", ArchiveDealAsync, "archiveDealCommand");
        MapPost(endpoints, "/deals/{dealId}/change-stage", ChangeStageAsync, "changeDealStageCommand");
        MapPost(endpoints, "/deals/{dealId}/mark-lost", MarkLostAsync, "markDealLostCommand");
        MapPost(endpoints, "/deals/{dealId}/mark-won", MarkWonAsync, "markDealWonCommand");
        MapPost(endpoints, "/deals/{dealId}/update", ReplaceProfileAsync, "updateDealCommand");
        MapPost(endpoints, "/deals/{dealId}/assign", AssignOwnerAsync, "assignDealOwner");
        MapPost(endpoints, "/deals/{dealId}/forecast", UpdateForecastAsync, "updateDealForecast");
        MapPost(endpoints, "/deals/{dealId}/next-action", UpdateNextActionAsync, "updateDealNextAction");
        return endpoints;
    }

    private static void MapGet(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapGet(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static void MapPost(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapPost(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static async Task<IResult> ListDealsAsync(
        HttpContext context,
        Application.ListDeals.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!DealsHttp.TryMetadata(context, false, false, out var metadata, out var error))
            return error!;
        int? limit = null;
        var suppliedLimit = Query(context, "limit");
        if (suppliedLimit is not null)
        {
            if (!int.TryParse(suppliedLimit, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                return DealsHttp.Error(
                    DealErrors.Validation(new Dictionary<string, string[]> { ["limit"] = ["limit must be an integer."] }),
                    metadata!.CorrelationId);
            }
            limit = parsed;
        }
        var result = await handler.HandleAsync(new Application.ListDeals.Query(
            Query(context, "cursor"),
            limit,
            Query(context, "search"),
            Query(context, "sortBy"),
            Query(context, "sortDirection"),
            Query(context, "stageCode"),
            Query(context, "stageCategory"),
            Query(context, "ownerId"),
            Query(context, "buyerType"),
            Query(context, "buyerId"),
            metadata!.RequestId,
            metadata.CorrelationId), cancellationToken);
        return DealsHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> GetForecastSummaryAsync(
        HttpContext context,
        Application.GetDealForecastSummary.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!DealsHttp.TryMetadata(context, false, false, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(new Application.GetDealForecastSummary.Query(
            Query(context, "ownerId"),
            Query(context, "buyerType"),
            Query(context, "buyerId"),
            metadata!.RequestId,
            metadata.CorrelationId), cancellationToken);
        return DealsHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> GetDealAsync(
        string dealId,
        HttpContext context,
        Application.GetDeal.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!DealsHttp.TryMetadata(context, false, false, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(
            new Application.GetDeal.Query(dealId, metadata!.RequestId, metadata.CorrelationId),
            cancellationToken);
        return DealsHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> CreateDealAsync(
        HttpContext context,
        Application.CreateDeal.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!DealsHttp.TryMetadata(context, true, false, out var metadata, out var error))
            return error!;
        var body = await DealsHttp.ReadBodyAsync<CreateDealRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await handler.HandleAsync(new Application.CreateDeal.Command(body.Value!, metadata), cancellationToken);
        return DealsHttp.Result(result, metadata.CorrelationId, StatusCodes.Status201Created);
    }

    private static Task<IResult> ReplaceProfileAsync(
        string dealId,
        HttpContext context,
        Application.ReplaceDealProfile.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync<ReplaceDealProfileRequest>(dealId, context, cancellationToken,
            (request, metadata) => handler.HandleAsync(new(dealId, request, metadata), cancellationToken));

    private static Task<IResult> ChangeStageAsync(
        string dealId,
        HttpContext context,
        Application.ChangeDealStage.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync<ChangeDealStageRequest>(dealId, context, cancellationToken,
            (request, metadata) => handler.HandleAsync(new(dealId, request, metadata), cancellationToken));

    private static Task<IResult> AssignOwnerAsync(
        string dealId,
        HttpContext context,
        Application.AssignDealOwner.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync<AssignDealOwnerRequest>(dealId, context, cancellationToken,
            (request, metadata) => handler.HandleAsync(new(dealId, request, metadata), cancellationToken));

    private static Task<IResult> UpdateForecastAsync(
        string dealId,
        HttpContext context,
        Application.UpdateDealForecast.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync<UpdateDealForecastRequest>(dealId, context, cancellationToken,
            (request, metadata) => handler.HandleAsync(new(dealId, request, metadata), cancellationToken));

    private static Task<IResult> UpdateNextActionAsync(
        string dealId,
        HttpContext context,
        Application.UpdateDealNextAction.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync<UpdateDealNextActionRequest>(dealId, context, cancellationToken,
            (request, metadata) => handler.HandleAsync(new(dealId, request, metadata), cancellationToken));

    private static Task<IResult> MarkWonAsync(
        string dealId,
        HttpContext context,
        Application.MarkDealWon.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync<MarkDealWonRequest>(dealId, context, cancellationToken,
            (request, metadata) => handler.HandleAsync(new(dealId, request, metadata), cancellationToken));

    private static Task<IResult> MarkLostAsync(
        string dealId,
        HttpContext context,
        Application.MarkDealLost.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync<MarkDealLostRequest>(dealId, context, cancellationToken,
            (request, metadata) => handler.HandleAsync(new(dealId, request, metadata), cancellationToken));

    private static Task<IResult> ArchiveDealAsync(
        string dealId,
        HttpContext context,
        Application.ArchiveDeal.Handler handler,
        CancellationToken cancellationToken) =>
        ExecuteCommandAsync<ArchiveDealRequest>(dealId, context, cancellationToken,
            (request, metadata) => handler.HandleAsync(new(dealId, request, metadata), cancellationToken));

    private static async Task<IResult> ArchiveBatchAsync(
        HttpContext context,
        Application.ArchiveDealsBatch.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!DealsHttp.TryMetadata(context, true, false, out var metadata, out var error))
            return error!;
        var body = await DealsHttp.ReadBodyAsync<ArchiveDealsBatchRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await handler.HandleAsync(new Application.ArchiveDealsBatch.Command(body.Value!, metadata), cancellationToken);
        return DealsHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> ExecuteCommandAsync<TRequest>(
        string dealId,
        HttpContext context,
        CancellationToken cancellationToken,
        Func<TRequest, DealCommandMetadata, Task<DealOperationResult<DealMutationResponse>>> execute)
        where TRequest : class
    {
        if (!DealsHttp.TryMetadata(context, true, true, out var metadata, out var error))
            return error!;
        var body = await DealsHttp.ReadBodyAsync<TRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await execute(body.Value!, metadata);
        return DealsHttp.Result(result, metadata.CorrelationId);
    }

    private static string? Query(HttpContext context, string key)
    {
        var value = context.Request.Query[key].ToString();
        return value.Length == 0 ? null : value;
    }
}

internal static class DealsHttp
{
    internal static bool TryMetadata(
        HttpContext context,
        bool requireIdempotency,
        bool requireIfMatch,
        out DealCommandMetadata? metadata,
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
            error = Error(DealErrors.Validation(fields), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new DealCommandMetadata(requestId, correlationId, idempotencyKey, expectedVersion);
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
        DealOperationResult<T> result,
        string correlationId,
        int successStatus = StatusCodes.Status200OK) =>
        result.IsSuccess ? Results.Json(result.Value, statusCode: successStatus) : Error(result.Error!, correlationId);

    internal static IResult Error(DealOperationError error, string correlationId) =>
        Results.Json(
            new DealProblemDetails(
                $"urn:unicore:error:{error.Code.ToLowerInvariant()}",
                error.Title,
                error.Status,
                error.Code,
                false,
                correlationId,
                error.Detail,
                null,
                error.FieldErrors,
                error.BusinessBlockers,
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
        Error(DealErrors.Validation(new Dictionary<string, string[]> { ["body"] = [message] }), correlationId);

    internal sealed record BodyRead<T>(T? Value, IResult? Error) where T : class;
}
