using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.Workspace.Application.Common;

namespace UnicoreCRM.Platform.Workspace.Contracts;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/workspaces", ListMyWorkspacesAsync)
            .RequireAuthorization()
            .WithName("listMyWorkspaces");
        endpoints.MapGet("/workspaces/{workspaceId}/bootstrap", GetWorkspaceBootstrapAsync)
            .RequireAuthorization()
            .WithName("getWorkspaceBootstrap");
        return endpoints;
    }

    private static async Task<IResult> ListMyWorkspacesAsync(
        HttpContext context,
        Application.ListMyWorkspaces.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!WorkspaceHttp.TryRequest(context, out var request, out var error))
            return error!;
        if (!TryPrincipal(context, out var accountId, out var memberId))
            return WorkspaceHttp.Error(WorkspaceErrors.AuthenticationRequired(), request!.CorrelationId);

        var result = await handler.HandleAsync(
            new Application.ListMyWorkspaces.Query(accountId, memberId, request!.CorrelationId),
            cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : WorkspaceHttp.Error(result.Error!, request.CorrelationId);
    }

    private static async Task<IResult> GetWorkspaceBootstrapAsync(
        string workspaceId,
        HttpContext context,
        Application.GetWorkspaceBootstrap.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!WorkspaceHttp.TryRequest(context, out var request, out var error))
            return error!;
        if (!WorkspaceIdContract.IsValid(workspaceId))
            return WorkspaceHttp.Error(WorkspaceErrors.WorkspaceMismatch(), request!.CorrelationId);
        if (!TryPrincipal(context, out var accountId, out var memberId))
            return WorkspaceHttp.Error(WorkspaceErrors.AuthenticationRequired(), request!.CorrelationId);

        var result = await handler.HandleAsync(
            new Application.GetWorkspaceBootstrap.Query(accountId, memberId, workspaceId, request!.CorrelationId),
            cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : WorkspaceHttp.Error(result.Error!, request.CorrelationId);
    }

    private static bool TryPrincipal(HttpContext context, out string accountId, out string memberId)
    {
        accountId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? string.Empty;
        memberId = context.User.FindFirst("member_id")?.Value ?? string.Empty;
        return accountId.Length != 0 && memberId.Length != 0;
    }

}

internal static class WorkspaceHttp
{
    internal static bool TryRequest(HttpContext context, out WorkspaceRequest? request, out IResult? error)
    {
        request = null;
        error = null;
        var requestId = context.Request.Headers["X-Request-Id"].ToString();
        var suppliedCorrelationId = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelationId.Length is >= 8 and <= 128
            ? suppliedCorrelationId
            : context.TraceIdentifier;
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (requestId.Length is < 8 or > 128)
            fields["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."];
        if (suppliedCorrelationId.Length != 0 && suppliedCorrelationId.Length is < 8 or > 128)
            fields["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."];
        if (fields.Count != 0)
        {
            error = Error(WorkspaceErrors.Validation(fields), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        request = new WorkspaceRequest(requestId, correlationId);
        return true;
    }

    internal static IResult Error(WorkspaceOperationError error, string correlationId) =>
        Results.Json(
            new WorkspaceProblemDetails(
                $"urn:unicore:error:{error.Code.ToLowerInvariant()}",
                error.Title,
                error.Status,
                error.Code,
                false,
                correlationId,
                error.Detail,
                null,
                error.FieldErrors),
            statusCode: error.Status,
            contentType: "application/problem+json");
}
