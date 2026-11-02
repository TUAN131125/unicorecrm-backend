using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Contracts;

internal static class QuoteDocumentEndpoints
{
    internal static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/quotes/document-seller", SellerAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("getQuoteDocumentSeller");
        endpoints.MapPost("/quotes/{quoteId}/documents/issue", IssueAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("issueQuoteDocumentCommand");
        endpoints.MapGet("/quotes/{quoteId}/documents", ListAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("listQuoteDocuments");
        endpoints.MapGet("/quotes/{quoteId}/documents/{documentId}", GetAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("getQuoteDocument");
        endpoints.MapGet("/quotes/{quoteId}/documents/{documentId}/content", DownloadAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("downloadQuoteDocument");
    }
    private static async Task<IResult> SellerAsync(HttpContext context, QuoteDocumentReads reads, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        return Result(await reads.SellerAsync(new(metadata!.RequestId, metadata.CorrelationId), ct), metadata.CorrelationId);
    }
    private static async Task<IResult> IssueAsync(string quoteId, HttpContext context, QuoteDocumentCommands commands, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var fields = new Dictionary<string, string[]>(); var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 128 || key.Any(char.IsControl)) fields["Idempotency-Key"] = ["Valid idempotency key required"];
        var match = context.Request.Headers.IfMatch.ToString(); long version = 0;
        if (match.Length < 3 || match[0] != '"' || match[^1] != '"' || !long.TryParse(match[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out version)) fields["If-Match"] = ["Strong resource version required"];
        if (fields.Count != 0) return QuotesHttp.Error(QuoteErrors.Validation(fields), metadata!.CorrelationId);
        string? reason = null;
        try
        {
            if (!context.Request.HasJsonContentType() || context.Request.ContentLength > 8192) throw new JsonException();
            // Bound chunked requests too: Content-Length is optional and is not authority.
            using var bounded = new MemoryStream(); var buffer = new byte[1024]; int count;
            while ((count = await context.Request.Body.ReadAsync(buffer, ct)) > 0)
            { if (bounded.Length + count > 8192) throw new JsonException(); await bounded.WriteAsync(buffer.AsMemory(0, count), ct); }
            bounded.Position = 0;
            using var json = await JsonDocument.ParseAsync(bounded, cancellationToken: ct); var body = json.RootElement;
            if (body.ValueKind != JsonValueKind.Object) throw new JsonException(); var values = body.EnumerateObject().ToArray();
            if (values.Length > 1 || values.Any(x => x.Name != "reason" || x.Value.ValueKind != JsonValueKind.String)) throw new JsonException();
            if (values.Length == 1) reason = values[0].Value.GetString();
        }
        catch (JsonException) { return QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["Issue accepts optional reason only"] }), metadata!.CorrelationId); }
        try
        {
            var response = await commands.IssueAsync(quoteId, new(reason), new(metadata!.RequestId, metadata.CorrelationId, key, version), ct);
            if (response.IsSuccess) context.Response.Headers.ETag = "\"" + response.Value!.Result.Quote.ResourceVersion.ToString(CultureInfo.InvariantCulture) + "\"";
            return Result(response, metadata.CorrelationId, 201);
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 1205 or 1222 or 51001)
        { return QuotesHttp.Error(new("IDEMPOTENCY_REQUEST_IN_PROGRESS", 409, "Document issuance is contended"), metadata!.CorrelationId); }
    }
    private static async Task<IResult> ListAsync(string quoteId, HttpContext context, QuoteDocumentReads reads, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var limit = context.Request.Query["limit"].FirstOrDefault(); var parsed = limit is null ? 20 : int.TryParse(limit, out var size) ? size : 0;
        return Result(await reads.ListAsync(quoteId, context.Request.Query["cursor"].FirstOrDefault(), parsed, new(metadata!.RequestId, metadata.CorrelationId), ct), metadata.CorrelationId);
    }
    private static async Task<IResult> GetAsync(string quoteId, string documentId, HttpContext context, QuoteDocumentReads reads, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        context.Response.Headers.CacheControl = "private, no-store";
        return Result(await reads.GetAsync(quoteId, documentId, new(metadata!.RequestId, metadata.CorrelationId), ct), metadata.CorrelationId);
    }
    private static async Task<IResult> DownloadAsync(string quoteId, string documentId, HttpContext context, QuoteDocumentReads reads, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var result = await reads.DownloadAsync(quoteId, documentId, new(metadata!.RequestId, metadata.CorrelationId), ct);
        if (!result.IsSuccess) return QuotesHttp.Error(result.Error!, metadata.CorrelationId);
        var file = result.Value!; context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers["X-Content-SHA256"] = file.Metadata.PdfSha256;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(file.Bytes, "application/pdf", file.Metadata.FileName, enableRangeProcessing: false);
    }
    private static IResult Result<T>(QuoteOperationResult<T> result, string correlationId, int status = 200) =>
        result.IsSuccess ? Results.Json(result.Value, statusCode: status) : QuotesHttp.Error(result.Error!, correlationId);
}
