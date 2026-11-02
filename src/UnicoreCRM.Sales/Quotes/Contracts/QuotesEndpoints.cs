using System.Globalization;
using System.Text.Json;
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
        endpoints.MapPost("/quotes", CreateAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("createQuoteCommand");
        endpoints.MapPost("/deals/{dealId}/quotes", CreateForDealAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("createQuoteForDeal");
        endpoints.MapPost("/quotes/{quoteId}/update-draft", UpdateAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("updateQuoteDraftCommand");
        endpoints.MapPost("/quotes/{quoteId}/request-approval", RequestApprovalAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("requestQuoteApprovalCommand");
        endpoints.MapPost("/quotes/{quoteId}/approve", ApproveAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("approveQuoteCommand");
        endpoints.MapPost("/quotes/{quoteId}/request-approval-changes", RequestChangesAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("requestQuoteApprovalChangesCommand");
        endpoints.MapPost("/quotes/{quoteId}/send", SendAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("recordQuoteSendEvidenceCommand");
        endpoints.MapPost("/quotes/{quoteId}/reject", RejectAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("rejectQuoteCommand");
        endpoints.MapPost("/quotes/{quoteId}/customer-decisions", RecordCustomerResponseAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("recordQuoteCustomerDecisionCommand");
        endpoints.MapPost("/workflows/quote-acceptance/{quoteId}/accept-and-close-deal", BlockAcceptanceAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("acceptQuoteAndCloseDeal");
        endpoints.MapPost("/quotes/{quoteId}/revise", ReviseAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("reviseQuoteCommand");
        endpoints.MapPost("/quotes/{quoteId}/expire", ExpireAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("expireQuoteCommand");
        endpoints.MapPost("/quotes/{quoteId}/archive", ArchiveAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("archiveQuoteCommand");
        QuoteDocumentEndpoints.Map(endpoints);
        QuoteVerifiedAcceptanceEndpoints.Map(endpoints);
        QuoteProofEndpoints.Map(endpoints);
        QuoteIdentityEndpoints.Map(endpoints);
        QuoteSalesAcceptanceEndpoints.Map(endpoints);
        return endpoints;
    }

    private static Task<IResult> ArchiveAsync(string quoteId, HttpContext context, QuoteArchiveCommands handler, CancellationToken ct) =>
        LifecycleAsync(context, true, 200, (reason, command) => handler.ExecuteAsync(quoteId, new(reason!), command, ct), ct);

    private static Task<IResult> ExpireAsync(string quoteId, HttpContext context, QuoteExpirationCommands handler, CancellationToken ct) =>
        LifecycleAsync(context, false, 200, (reason, command) => handler.ExecuteAsync(quoteId, new(reason), command, ct), ct);

    private static Task<IResult> ReviseAsync(string quoteId, HttpContext context, QuoteRevisionCommands handler, CancellationToken ct) =>
        LifecycleAsync(context, false, 201, (reason, command) => handler.ExecuteAsync(quoteId, new(reason), command, ct), ct);

    private static async Task<IResult> LifecycleAsync(HttpContext context, bool reasonRequired, int successStatus,
        Func<string?, QuoteCommandMetadata, Task<QuoteOperationResult<QuoteMutationResponse>>> execute, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var fields = new Dictionary<string, string[]>();
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 128 || key.Any(char.IsControl)) fields["Idempotency-Key"] = ["Valid idempotency key required."];
        var match = context.Request.Headers["If-Match"].ToString();
        long version = 0;
        if (match.Length < 3 || match[0] != '"' || match[^1] != '"'
            || !long.TryParse(match[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out version))
            fields["If-Match"] = ["A single strong quoted resource version is required."];
        if (fields.Count != 0) return QuotesHttp.Error(QuoteErrors.Validation(fields), metadata!.CorrelationId);
        string? reason = null;
        try
        {
            if (!context.Request.HasJsonContentType()) throw new JsonException();
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct);
            if (body.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var values = body.RootElement.EnumerateObject().ToArray();
            if (values.Length > 1 || values.Any(p => p.Name != "reason" || p.Value.ValueKind != JsonValueKind.String)) throw new JsonException();
            if (values.Length == 1) reason = values[0].Value.GetString();
            if (reasonRequired && string.IsNullOrWhiteSpace(reason)) throw new JsonException();
        }
        catch (JsonException) { return QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["Strict lifecycle request accepts only reason."] }), metadata!.CorrelationId); }
        try
        {
            var result = await execute(reason, new(metadata!.RequestId, metadata.CorrelationId, key, version));
            if (!result.IsSuccess) return QuotesHttp.Error(result.Error!, metadata.CorrelationId);
            context.Response.Headers.ETag = "\"" + result.Value!.Result.Quote.ResourceVersion.ToString(CultureInfo.InvariantCulture) + "\"";
            return Results.Json(result.Value, statusCode: successStatus);
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 1205 or 1222 or 51001)
        { return QuotesHttp.Error(new("IDEMPOTENCY_REQUEST_IN_PROGRESS", 409, "Quote command is temporarily contended"), metadata!.CorrelationId); }
    }

    private static async Task<IResult> RecordCustomerResponseAsync(string quoteId, HttpContext context, QuoteCustomerResponseCommands handler, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var fields = new Dictionary<string, string[]>();
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 128 || key.Any(char.IsControl)) fields["Idempotency-Key"] = ["Valid idempotency key required."];
        var match = context.Request.Headers["If-Match"].ToString();
        long version = 0;
        if (match.Length < 3 || match[0] != '"' || match[^1] != '"' || !long.TryParse(match[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out version))
            fields["If-Match"] = ["A single strong quoted resource version is required."];
        if (fields.Count != 0) return QuotesHttp.Error(QuoteErrors.Validation(fields), metadata!.CorrelationId);
        RecordQuoteCustomerDecisionRequest? request;
        try
        {
            if (!context.Request.HasJsonContentType()) throw new JsonException();
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct);
            if (body.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var properties = body.RootElement.EnumerateObject().ToArray();
            if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length
                || properties.Any(p => p.Name is not ("decisionId" or "evidenceType" or "sourceChannel" or "sourceDescription" or "reportedCustomerDecisionAt" or "note") || p.Value.ValueKind != JsonValueKind.String)) throw new JsonException();
            request = body.RootElement.Deserialize<RecordQuoteCustomerDecisionRequest>(QuotePricing.Json);
            if (request is null) throw new JsonException();
        }
        catch (JsonException) { return QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["Invalid strict customer response request; caller verification, Buyer and fingerprint claims are not admitted."] }), metadata!.CorrelationId); }
        try
        {
            return QuotesHttp.Result(await handler.ExecuteAsync(quoteId, request, new(metadata!.RequestId, metadata.CorrelationId, key, version), ct), metadata.CorrelationId);
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 1205 or 1222 or 51001)
        { return QuotesHttp.Error(new("IDEMPOTENCY_REQUEST_IN_PROGRESS", 409, "Quote command is temporarily contended"), metadata!.CorrelationId); }
    }

    private static async Task<IResult> BlockAcceptanceAsync(string quoteId, HttpContext context, QuoteAcceptanceGuard guard, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var fields = new Dictionary<string, string[]>();
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 128 || key.Any(char.IsControl)) fields["Idempotency-Key"] = ["Valid idempotency key required."];
        var match = context.Request.Headers["If-Match"].ToString();
        long version = 0;
        if (match.Length < 3 || match[0] != '"' || match[^1] != '"' || !long.TryParse(match[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out version))
            fields["If-Match"] = ["A single strong quoted resource version is required."];
        if (fields.Count != 0) return QuotesHttp.Error(QuoteErrors.Validation(fields), metadata!.CorrelationId);
        try
        {
            if (!context.Request.HasJsonContentType()) throw new JsonException();
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct);
            if (body.RootElement.ValueKind != JsonValueKind.Object || body.RootElement.EnumerateObject().Any()) throw new JsonException();
        }
        catch (JsonException) { return QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["The current acceptance workflow request must be an empty JSON object; client verification claims are not admitted."] }), metadata!.CorrelationId); }
        return QuotesHttp.Error(await guard.CheckAsync(quoteId, new(metadata!.RequestId, metadata.CorrelationId, key, version), ct), metadata.CorrelationId);
    }

    private static async Task<IResult> RejectAsync(string quoteId, HttpContext context, QuoteRejectionCommands handler, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var fields = new Dictionary<string, string[]>();
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 128 || key.Any(char.IsControl)) fields["Idempotency-Key"] = ["Valid idempotency key required."];
        var match = context.Request.Headers["If-Match"].ToString();
        long version = 0;
        if (match.Length < 3 || match[0] != '"' || match[^1] != '"' || !long.TryParse(match[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out version))
            fields["If-Match"] = ["A single strong quoted resource version is required."];
        if (fields.Count != 0) return QuotesHttp.Error(QuoteErrors.Validation(fields), metadata!.CorrelationId);
        RejectQuoteRequest? request;
        try
        {
            if (!context.Request.HasJsonContentType()) throw new JsonException();
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct);
            if (body.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var properties = body.RootElement.EnumerateObject().ToArray();
            if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length
                || properties.Any(p => p.Name is not ("reason" or "note") || p.Value.ValueKind != JsonValueKind.String)) throw new JsonException();
            request = body.RootElement.Deserialize<RejectQuoteRequest>(QuotePricing.Json);
            if (request is null) throw new JsonException();
        }
        catch (JsonException) { return QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["Invalid strict rejection request."] }), metadata!.CorrelationId); }
        try
        {
            return QuotesHttp.Result(await handler.ExecuteAsync(quoteId, request, new(metadata!.RequestId, metadata.CorrelationId, key, version), ct), metadata.CorrelationId);
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 1205 or 1222 or 51001)
        { return QuotesHttp.Error(new("IDEMPOTENCY_REQUEST_IN_PROGRESS", 409, "Quote command is temporarily contended"), metadata!.CorrelationId); }
    }

    private static async Task<IResult> SendAsync(string quoteId, HttpContext context, QuoteDeliveryCommands handler, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var fields = new Dictionary<string, string[]>();
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 128 || key.Any(char.IsControl)) fields["Idempotency-Key"] = ["Valid idempotency key required."];
        var match = context.Request.Headers["If-Match"].ToString();
        long version = 0;
        if (match.Length < 3 || match[0] != '"' || match[^1] != '"' || !long.TryParse(match[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out version))
            fields["If-Match"] = ["A single strong quoted resource version is required."];
        if (fields.Count != 0) return QuotesHttp.Error(QuoteErrors.Validation(fields), metadata!.CorrelationId);
        RecordQuoteSendEvidenceRequest? request;
        try
        {
            if (!context.Request.HasJsonContentType()) throw new JsonException();
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct);
            if (body.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var properties = body.RootElement.EnumerateObject().ToArray();
            if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length
                || properties.Any(p => p.Name is not ("deliveryId" or "channel" or "evidenceType" or "recipientEmail" or "recipient" or "note" or "sentAt" or "fileName") || p.Value.ValueKind != JsonValueKind.String)) throw new JsonException();
            request = body.RootElement.Deserialize<RecordQuoteSendEvidenceRequest>(QuotePricing.Json);
            if (request is null) throw new JsonException();
        }
        catch (JsonException) { return QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["Invalid strict delivery request."] }), metadata!.CorrelationId); }
        try
        {
            return QuotesHttp.Result(await handler.ExecuteAsync(quoteId, request, new(metadata!.RequestId, metadata.CorrelationId, key, version), ct), metadata.CorrelationId);
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 1205 or 1222 or 51001)
        { return QuotesHttp.Error(new("IDEMPOTENCY_REQUEST_IN_PROGRESS", 409, "Quote command is temporarily contended"), metadata!.CorrelationId); }
    }

    private static Task<IResult> RequestApprovalAsync(string quoteId, HttpContext context, QuoteApprovalCommands handler, CancellationToken ct) =>
        ApprovalAsync("requestQuoteApprovalCommand", quoteId, context, handler, ct);
    private static Task<IResult> ApproveAsync(string quoteId, HttpContext context, QuoteApprovalCommands handler, CancellationToken ct) =>
        ApprovalAsync("approveQuoteCommand", quoteId, context, handler, ct);
    private static Task<IResult> RequestChangesAsync(string quoteId, HttpContext context, QuoteApprovalCommands handler, CancellationToken ct) =>
        ApprovalAsync("requestQuoteApprovalChangesCommand", quoteId, context, handler, ct);
    private static async Task<IResult> ApprovalAsync(string operation, string quoteId, HttpContext context, QuoteApprovalCommands handler, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var fields = new Dictionary<string, string[]>();
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 128 || key.Any(char.IsControl)) fields["Idempotency-Key"] = ["Idempotency-Key must contain between eight and 128 characters."];
        var match = context.Request.Headers["If-Match"].ToString();
        long version = 0;
        if (match.Length < 3 || match[0] != '"' || match[^1] != '"' || !long.TryParse(match[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out version))
            fields["If-Match"] = ["A single strong quoted resource version is required."];
        if (fields.Count != 0) return QuotesHttp.Error(QuoteErrors.Validation(fields), metadata!.CorrelationId);
        if (!context.Request.HasJsonContentType()) return QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["JSON content is required."] }), metadata!.CorrelationId);
        string? note = null;
        try
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct);
            if (body.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var properties = body.RootElement.EnumerateObject().ToArray();
            if (properties.Any(x => x.Name != "note" || x.Value.ValueKind != JsonValueKind.String) || properties.Length > 1) throw new JsonException();
            if (body.RootElement.TryGetProperty("note", out var supplied)) note = supplied.GetString();
        }
        catch (JsonException) { return QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["Request body is invalid."] }), metadata!.CorrelationId); }
        try
        {
            var result = await handler.ExecuteAsync(operation, quoteId, note, new(metadata!.RequestId, metadata.CorrelationId, key, version), ct);
            return QuotesHttp.Result(result, metadata.CorrelationId);
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 1205 or 1222 or 51001)
        { return QuotesHttp.Error(new("IDEMPOTENCY_REQUEST_IN_PROGRESS", 409, "Quote command is temporarily contended"), metadata!.CorrelationId); }
    }

    private static Task<IResult> CreateAsync(HttpContext context, QuoteDraftCommands handler, CancellationToken ct) =>
        WriteAsync(context, handler, null, null, ct);
    private static Task<IResult> CreateForDealAsync(string dealId, HttpContext context, QuoteDraftCommands handler, CancellationToken ct) =>
        WriteAsync(context, handler, null, dealId, ct);
    private static Task<IResult> UpdateAsync(string quoteId, HttpContext context, QuoteDraftCommands handler, CancellationToken ct) =>
        WriteAsync(context, handler, quoteId, null, ct);
    private static async Task<IResult> WriteAsync(HttpContext context, QuoteDraftCommands handler, string? quoteId, string? dealId, CancellationToken ct)
    {
        if (!QuotesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var fields = new Dictionary<string, string[]>();
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length is < 8 or > 128 || key.Any(char.IsControl)) fields["Idempotency-Key"] = ["Idempotency-Key must contain between eight and 128 characters."];
        long? version = null;
        if (quoteId is not null || dealId is not null)
        {
            var match = context.Request.Headers["If-Match"].ToString();
            if (match.Length < 3 || match[0] != '"' || match[^1] != '"' || !long.TryParse(match[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) fields["If-Match"] = ["A single strong quoted resource version is required."];
            else version = parsed;
        }
        if (fields.Count != 0) return QuotesHttp.Error(QuoteErrors.Validation(fields), metadata!.CorrelationId);
        QuoteDraftRequest? request;
        if (!context.Request.HasJsonContentType()) return QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["JSON content is required."] }), metadata!.CorrelationId);
        try
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct);
            if (body.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            if (dealId is not null && body.RootElement.EnumerateObject().Any(property => property.Name is "buyerRef" or "sourcePath" or "sourceDealId")) throw new JsonException();
            request = body.RootElement.Deserialize<QuoteDraftRequest>(QuotePricing.Json);
        }
        catch (JsonException) { return QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["Request body is invalid."] }), metadata!.CorrelationId); }
        if (request is null) return QuotesHttp.Error(QuoteErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["Request body is required."] }), metadata!.CorrelationId);
        try
        {
            var result = await handler.ExecuteAsync(request, new(metadata!.RequestId, metadata.CorrelationId, key, version), quoteId, dealId, ct);
            return result.IsSuccess ? Results.Json(result.Value, statusCode: quoteId is null ? 201 : 200) : QuotesHttp.Error(result.Error!, metadata.CorrelationId);
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 1205 or 1222 or 51001)
        { return QuotesHttp.Error(new("IDEMPOTENCY_REQUEST_IN_PROGRESS", 409, "Quote command is temporarily contended"), metadata!.CorrelationId); }
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
            metadata!, Query(context, "rootQuoteId"), Query(context, "archived")), cancellationToken);
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
