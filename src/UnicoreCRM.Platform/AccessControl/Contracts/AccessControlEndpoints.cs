using System.Text;
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
        endpoints.MapPut("/access/roles/{roleId}", ReplaceAccessRoleAsync)
            .RequireAuthorization()
            .RequireTrustedWorkspaceWithDeferredRequestMetadata()
            .WithName("replaceAccessRole");
        endpoints.MapPost("/access/roles/{roleId}/archive", ArchiveAccessRoleAsync)
            .RequireAuthorization()
            .RequireTrustedWorkspaceWithDeferredRequestMetadata()
            .WithName("archiveAccessRole");
        endpoints.MapPost("/access/members/{membershipId}/access", ReplaceWorkspaceMemberAccessAsync)
            .RequireAuthorization()
            .RequireTrustedWorkspaceWithDeferredRequestMetadata()
            .WithName("replaceWorkspaceMemberAccess");
        endpoints.MapGet("/access/directory", GetWorkspaceAccessDirectoryAsync)
            .RequireAuthorization()
            .RequireTrustedWorkspaceWithDeferredRequestMetadata()
            .WithName("getWorkspaceAccessDirectory");
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
        var body = await AccessHttp.ReadAdministrativeBodyAsync(context.Request, cancellationToken);
        var result = await handler.HandleAsync(
            new Application.CreateAccessRole.Command(
                body,
                context.Request.Headers["X-Request-Id"].ToString(),
                correlationId,
                suppliedCorrelationId,
                context.Request.Headers["Idempotency-Key"].ToString()),
            cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : AccessHttp.Error(result.Error!, correlationId);
    }

    /// <summary>
    /// The route defers request-metadata validation to the handler so the frozen precedence holds:
    /// authentication, then the Trusted Workspace, then <c>access.configure</c>, and only then the
    /// required metadata and <c>If-Match</c> syntax. Validating metadata in the pipeline would let
    /// an unauthorized caller distinguish request shapes before the capability decision.
    /// </summary>
    private static async Task<IResult> ReplaceAccessRoleAsync(
        HttpContext context,
        string roleId,
        Application.ReplaceAccessRole.Handler handler,
        CancellationToken cancellationToken)
    {
        var suppliedCorrelationId = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelationId.Length is >= 8 and <= 128
            ? suppliedCorrelationId
            : context.TraceIdentifier;
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        var body = await AccessHttp.ReadAdministrativeBodyAsync(context.Request, cancellationToken);
        // The raw header value is used rather than the parsed typed header so a weak validator, a
        // wildcard, an unquoted value and a multi-value header all reach the frozen If-Match syntax
        // rule instead of being silently normalized away.
        var ifMatch = context.Request.Headers["If-Match"];
        var result = await handler.HandleAsync(
            new Application.ReplaceAccessRole.Command(
                roleId,
                body,
                context.Request.Headers["X-Request-Id"].ToString(),
                correlationId,
                suppliedCorrelationId,
                context.Request.Headers["Idempotency-Key"].ToString(),
                ifMatch.Count == 1 ? ifMatch[0] ?? string.Empty : string.Empty),
            cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : AccessHttp.Error(result.Error!, correlationId);
    }

    /// <summary>
    /// Like <c>replaceAccessRole</c>, this route defers request-metadata validation to the handler so
    /// the frozen precedence holds: authentication, the Trusted Workspace, <c>access.configure</c>,
    /// and only then the required metadata and <c>If-Match</c> syntax.
    /// </summary>
    private static async Task<IResult> ArchiveAccessRoleAsync(
        HttpContext context,
        string roleId,
        Application.ArchiveAccessRole.Handler handler,
        CancellationToken cancellationToken)
    {
        var suppliedCorrelationId = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelationId.Length is >= 8 and <= 128
            ? suppliedCorrelationId
            : context.TraceIdentifier;
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        var body = await AccessHttp.ReadAdministrativeBodyAsync(context.Request, cancellationToken);
        var ifMatch = context.Request.Headers["If-Match"];
        var result = await handler.HandleAsync(
            new Application.ArchiveAccessRole.Command(
                roleId,
                body,
                context.Request.Headers["X-Request-Id"].ToString(),
                correlationId,
                suppliedCorrelationId,
                context.Request.Headers["Idempotency-Key"].ToString(),
                ifMatch.Count == 1 ? ifMatch[0] ?? string.Empty : string.Empty),
            cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : AccessHttp.Error(result.Error!, correlationId);
    }

    private static async Task<IResult> GetWorkspaceAccessDirectoryAsync(
        HttpContext context,
        Application.GetWorkspaceAccessDirectory.Handler handler,
        CancellationToken cancellationToken)
    {
        var suppliedCorrelationId = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelationId.Length is >= 8 and <= 128
            ? suppliedCorrelationId
            : context.TraceIdentifier;
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        var result = await handler.HandleAsync(
            new Application.GetWorkspaceAccessDirectory.Query(
                context.Request.Headers["X-Request-Id"].ToString(),
                correlationId,
                suppliedCorrelationId),
            cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : AccessHttp.Error(result.Error!, correlationId);
    }

    /// <summary>
    /// Request metadata stays in the application stage so authorization precedes every request or
    /// target-membership validation and unauthorized callers learn no membership fact.
    /// </summary>
    private static async Task<IResult> ReplaceWorkspaceMemberAccessAsync(
        HttpContext context,
        string membershipId,
        Application.ReplaceWorkspaceMemberAccess.Handler handler,
        CancellationToken cancellationToken)
    {
        var suppliedCorrelationId = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelationId.Length is >= 8 and <= 128
            ? suppliedCorrelationId
            : context.TraceIdentifier;
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        var body = await AccessHttp.ReadAdministrativeBodyAsync(context.Request, cancellationToken);
        var ifMatch = context.Request.Headers["If-Match"];
        var result = await handler.HandleAsync(
            new Application.ReplaceWorkspaceMemberAccess.Command(
                membershipId,
                body,
                context.Request.Headers["X-Request-Id"].ToString(),
                correlationId,
                suppliedCorrelationId,
                context.Request.Headers["Idempotency-Key"].ToString(),
                ifMatch.Count == 1 ? ifMatch[0] ?? string.Empty : string.Empty),
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
    /// <summary>
    /// Access administration payloads are bounded by raw bytes, not decoded characters, so
    /// multibyte UTF-8 input cannot consume more request-buffer memory than single-byte input.
    /// </summary>
    internal const int MaximumAdministrativeRequestBodyBytes = 65_536;

    internal static async Task<AdministrativeRequestBody> ReadAdministrativeBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaximumAdministrativeRequestBodyBytes)
            return AdministrativeRequestBody.TooLarge;

        var buffer = new byte[MaximumAdministrativeRequestBodyBytes + 1];
        var read = await request.Body.ReadAtLeastAsync(
            buffer,
            buffer.Length,
            throwOnEndOfStream: false,
            cancellationToken);
        if (read > MaximumAdministrativeRequestBodyBytes)
            return AdministrativeRequestBody.TooLarge;

        using var stream = new MemoryStream(buffer, 0, read, writable: false, publiclyVisible: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return new AdministrativeRequestBody(await reader.ReadToEndAsync(cancellationToken), false);
    }

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
