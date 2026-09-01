using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Billing.Payments.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Billing.Payments.Contracts;

public static class PaymentsEndpoints
{
    public static IEndpointRouteBuilder MapPaymentsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/payment-plans", ListPaymentPlansAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("listPaymentPlans");
        endpoints.MapGet("/payment-schedule-lines", ListPaymentScheduleLinesAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("listPaymentScheduleLines");
        return endpoints;
    }

    private static async Task<IResult> ListPaymentPlansAsync(
        HttpContext context,
        Application.ListPaymentPlans.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!PaymentsHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var result = await handler.HandleAsync(new(Query(context, "orderId"), metadata!), cancellationToken);
        return PaymentsHttp.Result(result, metadata!.CorrelationId);
    }

    private static async Task<IResult> ListPaymentScheduleLinesAsync(
        HttpContext context,
        Application.ListPaymentScheduleLines.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!PaymentsHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var result = await handler.HandleAsync(new(Query(context, "planId"), metadata!), cancellationToken);
        return PaymentsHttp.Result(result, metadata!.CorrelationId);
    }

    private static string? Query(HttpContext context, string name)
    {
        var value = context.Request.Query[name].ToString();
        return value.Length == 0 ? null : value;
    }
}

internal static class PaymentsHttp
{
    internal static bool TryMetadata(HttpContext context, out PaymentRequestMetadata? metadata, out IResult? error)
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
            error = Error(PaymentErrors.Validation(fields, StatusCodes.Status400BadRequest), correlationId);
            return false;
        }
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new PaymentRequestMetadata(requestId, correlationId);
        return true;
    }

    internal static IResult Result<T>(PaymentOperationResult<T> result, string correlationId) =>
        result.IsSuccess ? Results.Json(result.Value) : Error(result.Error!, correlationId);

    internal static IResult Error(PaymentOperationError error, string correlationId) =>
        Results.Json(
            new PaymentProblemDetails(
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
