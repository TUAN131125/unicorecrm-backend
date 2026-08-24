using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Contracts;

public static class TasksEndpoints
{
    public static IEndpointRouteBuilder MapTasksEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapGet(endpoints, "/tasks", ListTasksAsync, "listTasks");
        MapPost(endpoints, "/tasks", CreateTaskAsync, "createTask");
        MapGet(endpoints, "/tasks/{taskId}", GetTaskAsync, "getTask");
        MapPost(endpoints, "/tasks/{taskId}/archive", ArchiveTaskAsync, "archiveTask");
        MapPost(endpoints, "/tasks/{taskId}/assign", AssignTaskAsync, "assignTask");
        MapPost(endpoints, "/tasks/{taskId}/cancel", CancelTaskAsync, "cancelTask");
        MapPost(endpoints, "/tasks/{taskId}/complete", CompleteTaskAsync, "completeTask");
        MapPost(endpoints, "/tasks/{taskId}/reschedule", RescheduleTaskAsync, "rescheduleTask");
        MapPost(endpoints, "/activities", LogActivityAsync, "logActivity");
        MapGet(endpoints, "/activities", ListActivitiesAsync, "listActivities");
        return endpoints;
    }

    private static void MapGet(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapGet(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static void MapPost(IEndpointRouteBuilder endpoints, string path, Delegate handler, string name) =>
        endpoints.MapPost(path, handler).RequireAuthorization().RequireTrustedWorkspace().WithName(name);

    private static async Task<IResult> ListTasksAsync(
        HttpContext context,
        Application.ListTasks.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!TasksHttp.TryMetadata(context, false, false, out var metadata, out var error))
            return error!;
        if (!TasksHttp.TryOptionalInt(context, "limit", metadata!.CorrelationId, out var limit, out error))
            return error!;
        var query = context.Request.Query;
        var result = await handler.HandleAsync(new Application.ListTasks.Query(
            Value(query, "cursor"), limit, Value(query, "search"), Value(query, "sortBy"),
            Value(query, "sortDirection"), Value(query, "status"), Value(query, "priority"),
            Value(query, "assigneeId"), Value(query, "relationshipType"), Value(query, "relationshipId"),
            Value(query, "recordModuleKey"), Value(query, "recordId"), Value(query, "overdueAt"),
            metadata.RequestId, metadata.CorrelationId), cancellationToken);
        return TasksHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> CreateTaskAsync(
        HttpContext context,
        Application.CreateTask.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!TasksHttp.TryMetadata(context, true, false, out var metadata, out var error))
            return error!;
        var body = await TasksHttp.ReadBodyAsync<CreateTaskRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var request = body.Value!;
        var result = await handler.HandleAsync(new Application.CreateTask.Command(request, metadata), cancellationToken);
        return TasksHttp.Result(result, metadata.CorrelationId, StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetTaskAsync(
        string taskId,
        HttpContext context,
        Application.GetTask.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!TasksHttp.TryMetadata(context, false, false, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(
            new Application.GetTask.Query(taskId, metadata!.RequestId, metadata.CorrelationId), cancellationToken);
        return TasksHttp.Result(result, metadata.CorrelationId);
    }

    private static Task<IResult> ArchiveTaskAsync(string taskId, HttpContext context, Application.ArchiveTask.Handler handler, CancellationToken token) =>
        ExecuteCommandAsync<ArchiveTaskRequest>(taskId, context, token, (request, metadata) => handler.HandleAsync(new(taskId, request, metadata), token));

    private static Task<IResult> AssignTaskAsync(string taskId, HttpContext context, Application.AssignTask.Handler handler, CancellationToken token) =>
        ExecuteCommandAsync<AssignTaskRequest>(taskId, context, token, (request, metadata) => handler.HandleAsync(new(taskId, request, metadata), token));

    private static Task<IResult> CancelTaskAsync(string taskId, HttpContext context, Application.CancelTask.Handler handler, CancellationToken token) =>
        ExecuteCommandAsync<CancelTaskRequest>(taskId, context, token, (request, metadata) => handler.HandleAsync(new(taskId, request, metadata), token));

    private static Task<IResult> CompleteTaskAsync(string taskId, HttpContext context, Application.CompleteTask.Handler handler, CancellationToken token) =>
        ExecuteCommandAsync<CompleteTaskRequest>(taskId, context, token, (request, metadata) => handler.HandleAsync(new(taskId, request, metadata), token));

    private static Task<IResult> RescheduleTaskAsync(string taskId, HttpContext context, Application.RescheduleTask.Handler handler, CancellationToken token) =>
        ExecuteCommandAsync<RescheduleTaskRequest>(taskId, context, token, (request, metadata) => handler.HandleAsync(new(taskId, request, metadata), token));

    private static async Task<IResult> LogActivityAsync(
        HttpContext context,
        Application.LogActivity.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!TasksHttp.TryMetadata(context, true, false, out var metadata, out var error))
            return error!;
        var body = await TasksHttp.ReadBodyAsync<LogActivityRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await handler.HandleAsync(new Application.LogActivity.Command(body.Value!, metadata), cancellationToken);
        return TasksHttp.Result(result, metadata.CorrelationId, StatusCodes.Status201Created);
    }

    private static async Task<IResult> ListActivitiesAsync(
        HttpContext context,
        Application.ListActivities.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!TasksHttp.TryMetadata(context, false, false, out var metadata, out var error))
            return error!;
        if (!TasksHttp.TryOptionalInt(context, "limit", metadata!.CorrelationId, out var limit, out error))
            return error!;
        var query = context.Request.Query;
        var result = await handler.HandleAsync(new Application.ListActivities.Query(
            Value(query, "cursor"), limit, Value(query, "search"), Value(query, "sortDirection"),
            Value(query, "type"), Value(query, "actorId"), Value(query, "relationshipType"),
            Value(query, "relationshipId"), Value(query, "recordModuleKey"), Value(query, "recordId"),
            Value(query, "occurredFrom"), Value(query, "occurredTo"), metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        return TasksHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> ExecuteCommandAsync<TRequest>(
        string taskId,
        HttpContext context,
        CancellationToken cancellationToken,
        Func<TRequest, TaskCommandMetadata, Task<TaskOperationResult<TaskMutationResponse>>> execute)
        where TRequest : class
    {
        if (!TasksHttp.TryMetadata(context, true, true, out var metadata, out var error))
            return error!;
        var body = await TasksHttp.ReadBodyAsync<TRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await execute(body.Value!, metadata);
        return TasksHttp.Result(result, metadata.CorrelationId);
    }

    private static string? Value(IQueryCollection query, string name) =>
        query.TryGetValue(name, out var value) ? value.ToString() : null;
}

internal static class TasksHttp
{
    internal static bool TryMetadata(
        HttpContext context,
        bool requireIdempotency,
        bool requireIfMatch,
        out TaskCommandMetadata? metadata,
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
            error = Error(TaskErrors.Validation(fields), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new TaskCommandMetadata(requestId, correlationId, idempotencyKey, expectedVersion);
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
            error = Error(TaskErrors.Validation(new Dictionary<string, string[]> { [name] = [$"{name} must be an integer."] }), correlationId);
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

    internal static IResult Result<T>(TaskOperationResult<T> result, string correlationId, int successStatus = StatusCodes.Status200OK) =>
        result.IsSuccess ? Results.Json(result.Value, statusCode: successStatus) : Error(result.Error!, correlationId);

    internal static IResult Error(TaskOperationError error, string correlationId) =>
        Results.Json(
            new TaskProblemDetails(
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
        Error(TaskErrors.Validation(new Dictionary<string, string[]> { ["body"] = [message] }), correlationId);

    internal sealed record BodyRead<T>(T? Value, IResult? Error) where T : class;
}
