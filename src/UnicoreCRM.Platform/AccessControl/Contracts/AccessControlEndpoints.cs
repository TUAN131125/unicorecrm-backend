using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Contracts;

public static class AccessControlEndpoints
{
    public static IEndpointRouteBuilder MapAccessControlEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/access/context", GetCurrentAuthorizationContextAsync)
            .RequireAuthorization()
            .RequireTrustedWorkspace()
            .WithName("getCurrentAuthorizationContext");
        return endpoints;
    }

    private static async Task<IResult> GetCurrentAuthorizationContextAsync(
        HttpContext context,
        Application.GetCurrentAuthorizationContext.Handler handler,
        CancellationToken cancellationToken)
    {
        var correlationId = CorrelationId(context);
        var result = await handler.HandleAsync(
            new Application.GetCurrentAuthorizationContext.Query(correlationId),
            cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : AccessHttp.Error(result.Error!, correlationId);
    }

    private static string CorrelationId(HttpContext context)
    {
        var supplied = context.Request.Headers["X-Correlation-Id"].ToString();
        return supplied.Length is >= 8 and <= 128 ? supplied : context.TraceIdentifier;
    }
}

internal static class AccessHttp
{
    internal static IResult Error(AccessOperationError error, string correlationId) =>
        Results.Json(
            new AccessProblemDetails(
                $"urn:unicore:error:{error.Code.ToLowerInvariant()}",
                error.Title,
                error.Status,
                error.Code,
                false,
                correlationId),
            statusCode: error.Status,
            contentType: "application/problem+json");
}
