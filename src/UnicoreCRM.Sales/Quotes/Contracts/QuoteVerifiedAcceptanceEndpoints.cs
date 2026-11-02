using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Contracts;

internal static class QuoteVerifiedAcceptanceEndpoints
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/quotes/{quoteId}/customer-acceptance", ReadAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("getQuoteCustomerAcceptanceReadiness");
        endpoints.MapPost("/quotes/{quoteId}/customer-acceptance/evaluate", EvaluateAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("evaluateQuoteCustomerAcceptance");
    }
    private static async Task<IResult> ReadAsync(string quoteId, HttpContext http, QuoteVerifiedAcceptanceAssessment handler, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(http, out var meta, out var error)) return error!;
        http.Response.Headers.CacheControl = "private, no-store";
        var result = await handler.ReadAsync(quoteId, http.Request.Query["documentId"].FirstOrDefault(), new(meta!.RequestId, meta.CorrelationId), ct);
        return result.IsSuccess ? Results.Json(result.Value) : QuotesHttp.Error(result.Error!, meta.CorrelationId);
    }
    private static async Task<IResult> EvaluateAsync(string quoteId, HttpContext http, QuoteVerifiedAcceptanceAssessment handler, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(http, out var meta, out var error)) return error!;
        http.Response.Headers.CacheControl = "private, no-store";
        var match = http.Request.Headers.IfMatch.ToString(); var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 128 || key.Any(char.IsControl) || match.Length < 3 || match[0] != '"' || match[^1] != '"'
            || !long.TryParse(match[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            return QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string, string[]> { ["headers"] = ["Strong If-Match and bounded request identity required"] }), meta!.CorrelationId);
        EvaluateQuoteCustomerAcceptanceRequest? request;
        try
        {
            if (!http.Request.HasJsonContentType() || http.Request.ContentLength > 8192) throw new JsonException();
            using var data = new MemoryStream(); var buffer = new byte[1024]; int count;
            while ((count = await http.Request.Body.ReadAsync(buffer, ct)) > 0)
            { if (data.Length + count > 8192) throw new JsonException(); await data.WriteAsync(buffer.AsMemory(0, count), ct); }
            request = JsonSerializer.Deserialize<EvaluateQuoteCustomerAcceptanceRequest>(data.ToArray(), QuotePricing.Json);
            if (request is null) throw new JsonException();
        }
        catch (JsonException) { return QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["Only typed assessment references are admitted; no verification flags or file bytes"] }), meta!.CorrelationId); }
        try
        {
            var result = await handler.EvaluateAsync(quoteId, request, new(meta!.RequestId, meta.CorrelationId, key, version), ct);
            return result.IsSuccess ? Results.Json(result.Value) : QuotesHttp.Error(result.Error!, meta.CorrelationId);
        }
        catch (Microsoft.Data.SqlClient.SqlException e) when (e.Number is 1205 or 1222 or 51001)
        { return QuotesHttp.Error(new("IDEMPOTENCY_REQUEST_IN_PROGRESS", 409, "Assessment is contended"), meta!.CorrelationId); }
    }
}
