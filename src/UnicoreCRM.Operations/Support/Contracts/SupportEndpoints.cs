using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Support.Contracts;

/// <summary>
/// The Support HTTP surface. It carries transport concerns only - header contract, body
/// binding, status mapping - and no Support business logic. Every route requires an
/// authenticated principal and a trusted Workspace before the use case is reached.
/// </summary>
public static class SupportEndpoints
{
    public static IEndpointRouteBuilder MapSupportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapGet(endpoints, "/support/cases", ListSupportCasesAsync, "listSupportCases");
        MapPost(endpoints, "/support/cases", CreateSupportCaseAsync, "createSupportCase");
        MapGet(endpoints, "/support/cases/{caseId}", GetSupportCaseAsync, "getSupportCase");
        MapPut(endpoints, "/support/cases/{caseId}", ReplaceSupportCaseProfileAsync, "replaceSupportCaseProfile");
        MapPost(endpoints, "/support/cases/{caseId}/assign", AssignSupportCaseAsync, "assignSupportCase");
        MapPost(endpoints, "/support/cases/{caseId}/transition", TransitionSupportCaseAsync, "transitionSupportCase");
        MapPost(endpoints, "/support/cases/{caseId}/replies", AddSupportCaseReplyAsync, "addSupportCaseReply");
        MapPost(endpoints, "/support/cases/{caseId}/internal-notes", AddSupportCaseInternalNoteAsync, "addSupportCaseInternalNote");
        return endpoints;
    }

    private static void MapGet(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapGet(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static void MapPost(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapPost(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static void MapPut(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapPut(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static async Task<IResult> ListSupportCasesAsync(
        HttpContext context,
        Application.ListSupportCases.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!SupportHttp.TryMetadata(context, false, false, out var metadata, out var error))
            return error!;
        if (!SupportHttp.TryOptionalInt(context, "limit", metadata!.CorrelationId, out var limit, out error))
            return error!;
        var query = context.Request.Query;
        var result = await handler.HandleAsync(new Application.ListSupportCases.Query(
            Value(query, "cursor"), limit, Value(query, "search"), Value(query, "sortBy"),
            Value(query, "sortDirection"), Value(query, "status"), Value(query, "priority"),
            Value(query, "category"), Value(query, "ownerId"), Value(query, "relationshipType"),
            Value(query, "relationshipId"), Value(query, "slaStatus"),
            metadata.RequestId, metadata.CorrelationId), cancellationToken);
        return SupportHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> CreateSupportCaseAsync(
        HttpContext context,
        Application.CreateSupportCase.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!SupportHttp.TryMetadata(context, true, false, out var metadata, out var error))
            return error!;
        var body = await SupportHttp.ReadBodyAsync<CreateSupportCaseRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await handler.HandleAsync(new Application.CreateSupportCase.Command(body.Value!, metadata), cancellationToken);
        return SupportHttp.Result(result, metadata.CorrelationId, StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetSupportCaseAsync(
        string caseId,
        HttpContext context,
        Application.GetSupportCase.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!SupportHttp.TryMetadata(context, false, false, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(
            new Application.GetSupportCase.Query(caseId, metadata!.RequestId, metadata.CorrelationId), cancellationToken);
        return SupportHttp.Result(result, metadata.CorrelationId);
    }

    private static Task<IResult> ReplaceSupportCaseProfileAsync(
        string caseId,
        HttpContext context,
        Application.ReplaceSupportCaseProfile.Handler handler,
        CancellationToken token) =>
        ExecuteCommandAsync<ReplaceSupportCaseProfileRequest>(
            context, token, (request, metadata) => handler.HandleAsync(new(caseId, request, metadata), token));

    private static Task<IResult> AssignSupportCaseAsync(
        string caseId,
        HttpContext context,
        Application.AssignSupportCase.Handler handler,
        CancellationToken token) =>
        ExecuteCommandAsync<AssignSupportCaseRequest>(
            context, token, (request, metadata) => handler.HandleAsync(new(caseId, request, metadata), token));

    private static Task<IResult> TransitionSupportCaseAsync(
        string caseId,
        HttpContext context,
        Application.TransitionSupportCase.Handler handler,
        CancellationToken token) =>
        ExecuteCommandAsync<TransitionSupportCaseRequest>(
            context, token, (request, metadata) => handler.HandleAsync(new(caseId, request, metadata), token));

    private static Task<IResult> AddSupportCaseReplyAsync(
        string caseId,
        HttpContext context,
        Application.AddSupportCaseReply.Handler handler,
        CancellationToken token) =>
        ExecuteCommandAsync<AddSupportCaseReplyRequest>(
            context, token, (request, metadata) => handler.HandleAsync(new(caseId, request, metadata), token));

    private static Task<IResult> AddSupportCaseInternalNoteAsync(
        string caseId,
        HttpContext context,
        Application.AddSupportCaseInternalNote.Handler handler,
        CancellationToken token) =>
        ExecuteCommandAsync<AddSupportCaseInternalNoteRequest>(
            context, token, (request, metadata) => handler.HandleAsync(new(caseId, request, metadata), token));

    private static async Task<IResult> ExecuteCommandAsync<TRequest>(
        HttpContext context,
        CancellationToken cancellationToken,
        Func<TRequest, SupportCommandMetadata, Task<SupportOperationResult<SupportCaseMutationResponse>>> execute)
        where TRequest : class
    {
        if (!SupportHttp.TryMetadata(context, true, true, out var metadata, out var error))
            return error!;
        var body = await SupportHttp.ReadBodyAsync<TRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await execute(body.Value!, metadata);
        return SupportHttp.Result(result, metadata.CorrelationId);
    }

    private static string? Value(IQueryCollection query, string name) =>
        query.TryGetValue(name, out var value) ? value.ToString() : null;
}

internal static class SupportHttp
{
    internal static bool TryMetadata(
        HttpContext context,
        bool requireIdempotency,
        bool requireIfMatch,
        out SupportCommandMetadata? metadata,
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
            error = Error(SupportErrors.Validation(fields), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new SupportCommandMetadata(requestId, correlationId, idempotencyKey, expectedVersion);
        return true;
    }

    internal static bool TryOptionalInt(
        HttpContext context,
        string name,
        string correlationId,
        out int? value,
        out IResult? error)
    {
        value = null;
        error = null;
        if (!context.Request.Query.TryGetValue(name, out var supplied))
            return true;
        if (supplied.Count != 1 || !int.TryParse(supplied.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            error = Error(SupportErrors.Validation(new Dictionary<string, string[]> { [name] = [$"{name} must be an integer."] }), correlationId);
            return false;
        }
        value = parsed;
        return true;
    }

    internal static async Task<BodyRead<T>> ReadBodyAsync<T>(HttpContext context, string correlationId, CancellationToken cancellationToken)
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

    internal static IResult Result<T>(SupportOperationResult<T> result, string correlationId, int successStatus = StatusCodes.Status200OK) =>
        result.IsSuccess ? Results.Json(result.Value, statusCode: successStatus) : Error(result.Error!, correlationId);

    internal static IResult Error(SupportOperationError error, string correlationId) =>
        Results.Json(
            new SupportProblemDetails(
                $"urn:unicore:error:{error.Code.ToLowerInvariant()}", error.Title, error.Status, error.Code,
                false, correlationId, error.Detail, null, error.FieldErrors, error.AggregateId,
                error.ExpectedVersion, error.CurrentVersion, error.IdempotencyKey),
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
        Error(SupportErrors.Validation(new Dictionary<string, string[]> { ["body"] = [message] }), correlationId);

    internal sealed record BodyRead<T>(T? Value, IResult? Error) where T : class;
}
