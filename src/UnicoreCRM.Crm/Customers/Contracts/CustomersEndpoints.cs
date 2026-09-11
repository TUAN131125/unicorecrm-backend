using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Customers.Contracts;

public static class CustomersEndpoints
{
    public static IEndpointRouteBuilder MapCustomersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/customers", ListCustomersAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("listCustomers");
        endpoints.MapGet("/customers/{customerId}", GetCustomerAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getCustomer");
        endpoints.MapGet("/customers/{customerId}/360", GetCustomer360Async)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getCustomer360");
        endpoints.MapPost("/customers", CreateCustomerAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("createCustomer");
        endpoints.MapPatch("/customers/{customerId}", UpdateCustomerAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("updateCustomer");
        endpoints.MapPost("/customers/{customerId}/archive", ArchiveCustomerAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("archiveCustomer");
        return endpoints;
    }

    private static async Task<IResult> ListCustomersAsync(
        HttpContext context,
        Application.ListCustomers.Handler handler,
        CancellationToken cancellationToken,
        string? q = null, string? type = null, string? status = null, string? ownerId = null,
        string? segment = null, string? tier = null, string? cursor = null, int limit = 50)
    {
        if (!CustomersHttp.TryMetadata(context, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(new(metadata!, q, type, status, ownerId, segment, tier, cursor, limit), cancellationToken);
        return CustomersHttp.Result(result, metadata!.CorrelationId);
    }

    private static async Task<IResult> GetCustomerAsync(
        string customerId,
        HttpContext context,
        Application.GetCustomer.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!CustomersHttp.TryMetadata(context, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(new(customerId, metadata!), cancellationToken);
        if (result.IsSuccess) context.Response.Headers.ETag = $"\"{result.Value!.Version}\"";
        return CustomersHttp.Result(result, metadata!.CorrelationId);
    }

    private static async Task<IResult> GetCustomer360Async(string customerId, HttpContext context,
        Application.GetCustomer360.Handler handler, CancellationToken cancellationToken)
    {
        if (!CustomersHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var result = await handler.HandleAsync(new(customerId, metadata!), cancellationToken);
        if (result.IsSuccess) context.Response.Headers.ETag = $"\"{result.Value!.ProjectionVersion}\"";
        return CustomersHttp.Result(result, metadata!.CorrelationId);
    }

    private static async Task<IResult> CreateCustomerAsync(HttpContext context, Application.CreateCustomer.Handler handler, CancellationToken cancellationToken)
    {
        if (!CustomersHttp.TryCommandMetadata(context, false, out var metadata, out var error)) return error!;
        var body = await CustomersHttp.ReadBodyAsync<CreateCustomerRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null) return body.Error;
        var result = await handler.HandleAsync(new(body.Value!, metadata), cancellationToken);
        if (result.IsSuccess) context.Response.Headers.ETag = $"\"{result.Value!.Version}\"";
        return CustomersHttp.Result(result, metadata.CorrelationId, StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateCustomerAsync(string customerId, HttpContext context, Application.UpdateCustomer.Handler handler, CancellationToken cancellationToken)
    {
        if (!CustomersHttp.TryCommandMetadata(context, true, out var metadata, out var error)) return error!;
        var body = await CustomersHttp.ReadBodyAsync<UpdateCustomerRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null) return body.Error;
        var result = await handler.HandleAsync(new(customerId, body.Value!, metadata), cancellationToken);
        if (result.IsSuccess) context.Response.Headers.ETag = $"\"{result.Value!.Version}\"";
        return CustomersHttp.Result(result, metadata.CorrelationId);
    }

    private static async Task<IResult> ArchiveCustomerAsync(string customerId, HttpContext context, Application.ArchiveCustomer.Handler handler, CancellationToken cancellationToken)
    {
        if (!CustomersHttp.TryCommandMetadata(context, true, out var metadata, out var error)) return error!;
        var body = await CustomersHttp.ReadBodyAsync<ArchiveCustomerRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null) return body.Error;
        var result = await handler.HandleAsync(new(customerId, metadata!), cancellationToken);
        if (result.IsSuccess) context.Response.Headers.ETag = $"\"{result.Value!.Version}\"";
        return CustomersHttp.Result(result, metadata!.CorrelationId);
    }
}

internal static class CustomersHttp
{
    internal static bool TryMetadata(
        HttpContext context,
        out CustomerRequestMetadata? metadata,
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
            error = Error(CustomerErrors.Validation(fields, StatusCodes.Status400BadRequest), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new CustomerRequestMetadata(requestId, correlationId);
        return true;
    }

    internal static IResult Result<T>(CustomerOperationResult<T> result, string correlationId, int successStatus = StatusCodes.Status200OK) =>
        result.IsSuccess ? Results.Json(result.Value, statusCode: successStatus) : Error(result.Error!, correlationId);

    internal static bool TryCommandMetadata(HttpContext context, bool requireIfMatch, out CustomerCommandMetadata? metadata, out IResult? error)
    {
        metadata = null; if (!TryMetadata(context, out var read, out error)) return false;
        var key = context.Request.Headers["Idempotency-Key"].ToString(); var match = context.Request.Headers.IfMatch.ToString();
        var fields = new Dictionary<string, string[]>();
        if (key.Length is < 8 or > 128) fields["Idempotency-Key"] = ["Idempotency-Key must contain between 8 and 128 characters."];
        long? expected = null; if (requireIfMatch && !TryExpectedVersion(match, out expected)) fields["If-Match"] = ["If-Match must contain a quoted non-negative resource version."];
        if (fields.Count > 0) { error = Error(CustomerErrors.Validation(fields, 400), read!.CorrelationId); return false; }
        metadata = new(read!.RequestId, read.CorrelationId, key, expected); return true;
    }

    internal static async Task<CustomerBodyRead<T>> ReadBodyAsync<T>(HttpContext context, string correlationId, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
            return value is null ? new(null, Error(CustomerErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["A JSON request body is required."] }, 400), correlationId)) : new(value, null);
        }
        catch (JsonException) { return new(null, Error(CustomerErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["The JSON body is invalid."] }, 400), correlationId)); }
    }

    private static bool TryExpectedVersion(string value, out long? version)
    {
        version = null; if (value.Length < 3 || value[0] != '"' || value[^1] != '"' || !long.TryParse(value[1..^1], out var parsed) || parsed < 0) return false;
        version = parsed; return true;
    }

    private static IResult Error(CustomerOperationError error, string correlationId) =>
        Results.Json(
            new CustomerProblemDetails(
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

internal sealed record CustomerBodyRead<T>(T? Value, IResult? Error) where T : class;

internal sealed record CustomerProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? FieldErrors = null);
