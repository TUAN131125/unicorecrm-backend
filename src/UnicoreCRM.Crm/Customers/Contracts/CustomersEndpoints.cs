using System.Text.Json.Serialization;
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
        return endpoints;
    }

    private static async Task<IResult> ListCustomersAsync(
        HttpContext context,
        Application.ListCustomers.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!CustomersHttp.TryMetadata(context, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(new(metadata!), cancellationToken);
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

    internal static IResult Result<T>(CustomerOperationResult<T> result, string correlationId) =>
        result.IsSuccess ? Results.Json(result.Value) : Error(result.Error!, correlationId);

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

internal sealed record CustomerProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? FieldErrors = null);
