using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Billing.Invoices.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Billing.Invoices.Contracts;

/// <summary>
/// The exact admitted Invoice read surface: <c>listInvoices</c> and <c>getInvoice</c>. No other
/// Invoice route is mapped, and no mutation route exists in this slice.
/// </summary>
public static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/invoices", ListInvoicesAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("listInvoices");
        endpoints.MapGet("/invoices/{invoiceId}", GetInvoiceAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getInvoice");
        return endpoints;
    }

    private static async Task<IResult> ListInvoicesAsync(
        HttpContext context,
        Application.ListInvoices.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!InvoicesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var result = await handler.HandleAsync(new(metadata!), cancellationToken);
        return InvoicesHttp.Result(result, metadata!.CorrelationId);
    }

    private static async Task<IResult> GetInvoiceAsync(
        string invoiceId,
        HttpContext context,
        Application.GetInvoice.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!InvoicesHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var result = await handler.HandleAsync(new(invoiceId, metadata!), cancellationToken);
        return InvoicesHttp.Result(result, metadata!.CorrelationId);
    }
}

internal static class InvoicesHttp
{
    internal static bool TryMetadata(HttpContext context, out InvoiceRequestMetadata? metadata, out IResult? error)
    {
        metadata = null;
        error = null;
        var requestId = context.Request.Headers["X-Request-Id"].ToString();
        var suppliedCorrelation = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelation.Length is >= 8 and <= 128 ? suppliedCorrelation : context.TraceIdentifier;
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (requestId.Length is < 8 or > 128)
            fields["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."];
        if (suppliedCorrelation.Length != 0 && suppliedCorrelation.Length is < 8 or > 128)
            fields["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."];
        if (fields.Count != 0)
        {
            error = Error(InvoiceErrors.Validation(fields, StatusCodes.Status400BadRequest), correlationId);
            return false;
        }
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new InvoiceRequestMetadata(requestId, correlationId);
        return true;
    }

    internal static IResult Result<T>(InvoiceOperationResult<T> result, string correlationId) =>
        result.IsSuccess ? Results.Json(result.Value) : Error(result.Error!, correlationId);

    internal static IResult Error(InvoiceOperationError error, string correlationId) =>
        Results.Json(
            new InvoiceProblemDetails(
                $"urn:unicore:error:{error.Code.ToLowerInvariant()}",
                error.Title,
                error.Status,
                error.Code,
                false,
                correlationId,
                FieldErrors: error.FieldErrors),
            statusCode: error.Status,
            contentType: "application/problem+json");
}
