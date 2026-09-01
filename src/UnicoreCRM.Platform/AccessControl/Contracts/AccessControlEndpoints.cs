using System.Text.Json;
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
        endpoints.MapPost("/access/records/evaluate", EvaluateEffectiveRecordAccessAsync)
            .RequireAuthorization()
            .RequireTrustedWorkspace()
            .WithName("evaluateEffectiveRecordAccess");
        endpoints.MapPost("/access/roles", CreateAccessRoleAsync)
            .RequireAuthorization()
            .RequireTrustedWorkspace()
            .WithName("createAccessRole");
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

    private static async Task<IResult> EvaluateEffectiveRecordAccessAsync(
        HttpContext context,
        Application.EvaluateEffectiveRecordAccess.Handler handler,
        CancellationToken cancellationToken)
    {
        var correlationId = CorrelationId(context);
        var requestId = context.Request.Headers["X-Request-Id"].ToString();
        if (requestId.Length is < 8 or > 128)
        {
            return AccessHttp.Error(
                AccessErrors.Validation(new Dictionary<string, string[]>
                {
                    ["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."]
                }),
                correlationId);
        }

        EvaluateEffectiveRecordAccessRequest? body;
        try
        {
            body = await context.Request.ReadFromJsonAsync<EvaluateEffectiveRecordAccessRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            // Unmapped members are disallowed on the request contract, so a caller-supplied
            // workspace, owner or team fact lands here rather than being silently ignored.
            return AccessHttp.Error(
                AccessErrors.Validation(new Dictionary<string, string[]>
                {
                    ["body"] = ["The JSON request body does not match the contract."]
                }),
                correlationId);
        }
        catch (NotSupportedException)
        {
            body = null;
        }

        if (body is null)
        {
            return AccessHttp.Error(
                AccessErrors.Validation(new Dictionary<string, string[]>
                {
                    ["body"] = ["A JSON request body is required."]
                }),
                correlationId);
        }

        var result = await handler.HandleAsync(
            new Application.EvaluateEffectiveRecordAccess.Query(body, requestId, correlationId),
            cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : AccessHttp.Error(result.Error!, correlationId);
    }

    private static async Task<IResult> CreateAccessRoleAsync(
        HttpContext context,
        Application.CreateAccessRole.Handler handler,
        CancellationToken cancellationToken)
    {
        var suppliedCorrelationId = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelationId.Length is >= 8 and <= 128
            ? suppliedCorrelationId
            : context.TraceIdentifier;
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        using var reader = new StreamReader(context.Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        var result = await handler.HandleAsync(
            new Application.CreateAccessRole.Command(
                rawBody,
                context.Request.Headers["X-Request-Id"].ToString(),
                correlationId,
                suppliedCorrelationId,
                context.Request.Headers["Idempotency-Key"].ToString()),
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
                correlationId,
                FieldErrors: error.FieldErrors,
                IdempotencyKey: error.IdempotencyKey),
            statusCode: error.Status,
            contentType: "application/problem+json");
}
