using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Billing.Payments.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Billing.Payments.Contracts;

public static class PaymentIntentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentIntentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/payment-intents", ListPaymentIntentsAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("listPaymentIntents");
        endpoints.MapGet("/payment-intents/{intentId}", GetPaymentIntentAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getPaymentIntent");
        endpoints.MapGet("/payment-intents/{intentId}/status", GetPaymentIntentStatusAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getPaymentIntentStatus");
        return endpoints;
    }

    private static async Task<IResult> ListPaymentIntentsAsync(
        HttpContext context,
        Application.ListPaymentIntents.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!PaymentsHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var orderId = context.Request.Query["orderId"].ToString();
        var result = await handler.HandleAsync(new(orderId.Length == 0 ? null : orderId, metadata!), cancellationToken);
        return PaymentsHttp.Result(result, metadata!.CorrelationId);
    }

    private static async Task<IResult> GetPaymentIntentAsync(
        string intentId,
        HttpContext context,
        Application.GetPaymentIntent.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!PaymentsHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var result = await handler.HandleAsync(new(intentId, metadata!), cancellationToken);
        return PaymentsHttp.Result(result, metadata!.CorrelationId);
    }

    private static async Task<IResult> GetPaymentIntentStatusAsync(
        string intentId,
        HttpContext context,
        Application.GetPaymentIntentStatus.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!PaymentsHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var result = await handler.HandleAsync(new(intentId, metadata!), cancellationToken);
        return PaymentsHttp.Result(result, metadata!.CorrelationId);
    }
}
