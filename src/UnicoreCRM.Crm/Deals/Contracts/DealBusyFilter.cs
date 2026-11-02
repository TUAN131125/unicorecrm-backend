using Microsoft.AspNetCore.Http;
using UnicoreCRM.Crm.Deals.Application.Common;

namespace UnicoreCRM.Crm.Deals.Contracts;

internal sealed class DealBusyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (DealsPersistenceBusyException)
        {
            var correlation = context.HttpContext.Response.Headers["X-Correlation-Id"].ToString();
            return Results.Json(new DealProblemDetails("urn:unicore:error:resource_busy", "Resource is busy", 409,
                "RESOURCE_BUSY", true, correlation.Length == 0 ? context.HttpContext.TraceIdentifier : correlation),
                statusCode: 409, contentType: "application/problem+json");
        }
    }
}
