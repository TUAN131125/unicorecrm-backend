using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Billing.Payments.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Billing.Payments.Contracts;

public static class PaymentRecordEndpoints
{
    public static IEndpointRouteBuilder MapPaymentRecordEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/payments", ListPaymentRecordsAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("listPaymentRecords");
        endpoints.MapGet("/payments/{paymentRecordId}/detail", GetPaymentRecordDetailAsync)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getPaymentRecordDetail");
        return endpoints;
    }

    private static async Task<IResult> ListPaymentRecordsAsync(
        HttpContext context,
        Application.ListPaymentRecords.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!PaymentsHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var buyerId = context.Request.Query.ContainsKey("buyerId")
            ? context.Request.Query["buyerId"].ToString()
            : null;
        var result = await handler.HandleAsync(new(buyerId, metadata!), cancellationToken);
        return PaymentsHttp.Result(result, metadata!.CorrelationId);
    }

    private static async Task<IResult> GetPaymentRecordDetailAsync(
        string paymentRecordId,
        HttpContext context,
        Application.GetPaymentRecordDetail.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!PaymentsHttp.TryMetadata(context, out var metadata, out var error)) return error!;
        var result = await handler.HandleAsync(new(paymentRecordId, metadata!), cancellationToken);
        return PaymentsHttp.Result(result, metadata!.CorrelationId);
    }
}
