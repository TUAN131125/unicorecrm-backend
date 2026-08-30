using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;

namespace UnicoreCRM.Sales.Quotes.Contracts;

public static class QuotesEndpoints
{
    public static IEndpointRouteBuilder MapQuotesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/quotes", ListQuotesAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("listQuotes");
        endpoints.MapGet("/quotes/{quoteId}", GetQuoteAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getQuote");
        return endpoints;
    }

    private static async Task<IResult> ListQuotesAsync(
        HttpContext context,
        Application.ListQuotes.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error))
            return error!;

        int? limit = null;
        var suppliedLimit = Query(context, "limit");
        if (suppliedLimit is not null)
        {
            if (!int.TryParse(suppliedLimit, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                return QuotesHttp.Error(
                    QuoteErrors.Validation(new Dictionary<string, string[]> { ["limit"] = ["limit must be an integer."] }),
                    metadata!.CorrelationId);
            }
            limit = parsed;
        }

        var result = await handler.HandleAsync(new(
            Query(context, "cursor"),
            limit,
            Query(context, "search"),
            Query(context, "sortBy"),
            Query(context, "sortDirection"),
            Query(context, "status"),
            Query(context, "sourceDealId"),
            Query(context, "buyerType"),
            Query(context, "buyerId"),
            metadata!), cancellationToken);
        return QuotesHttp.Result(result, metadata!.CorrelationId);
    }

    private static async Task<IResult> GetQuoteAsync(
        string quoteId,
        HttpContext context,
        Application.GetQuote.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error))
            return error!;
        var result = await handler.HandleAsync(new(quoteId, metadata!), cancellationToken);
        return QuotesHttp.Result(result, metadata!.CorrelationId);
    }

    private static string? Query(HttpContext context, string key)
    {
        var value = context.Request.Query[key].ToString();
        return value.Length == 0 ? null : value;
    }
}

internal static class QuotesHttp
{
    internal static bool TryMetadata(
        HttpContext context,
        out QuoteRequestMetadata? metadata,
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
            error = Error(QuoteErrors.Validation(fields, StatusCodes.Status400BadRequest), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new QuoteRequestMetadata(requestId, correlationId);
        return true;
    }

    internal static IResult Result<T>(QuoteOperationResult<T> result, string correlationId) =>
        result.IsSuccess ? Results.Json(result.Value) : Error(result.Error!, correlationId);

    internal static IResult Error(QuoteOperationError error, string correlationId) =>
        Results.Json(
            new QuoteProblemDetails(
                $"urn:unicore:error:{error.Code.ToLowerInvariant()}",
                error.Title,
                error.Status,
                error.Code,
                false,
                correlationId,
                error.Detail,
                FieldErrors: error.FieldErrors),
            statusCode: error.Status,
            contentType: "application/problem+json");
}
