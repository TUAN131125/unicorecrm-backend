using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Platform.IdentityAuth.Application.Common;

namespace UnicoreCRM.Platform.IdentityAuth.Contracts;

public static class IdentityAuthEndpoints
{
    private const string RefreshCookieName = "__Host-unicore-refresh";

    public static IEndpointRouteBuilder MapIdentityAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/accounts", RegisterAccountAsync).AllowAnonymous().WithName("registerAccount");
        endpoints.MapPost("/auth/email-verification-requests", RequestEmailVerificationAsync).AllowAnonymous().WithName("requestEmailVerification");
        endpoints.MapPost("/auth/email-verifications", VerifyEmailAsync).AllowAnonymous().WithName("verifyEmail");
        endpoints.MapPost("/auth/sessions", SignInAsync).AllowAnonymous().WithName("signIn");
        endpoints.MapGet("/auth/session", GetCurrentSessionAsync).RequireAuthorization().WithName("getCurrentSession");
        endpoints.MapPost("/auth/session/refresh", RefreshSessionAsync).AllowAnonymous().WithName("refreshSession");
        endpoints.MapPost("/auth/session/logout", SignOutAsync).RequireAuthorization().WithName("signOut");
        return endpoints;
    }

    private static async Task<IResult> RegisterAccountAsync(
        HttpContext httpContext,
        Application.RegisterAccount.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!IdentityHttp.TryMetadata(httpContext, true, out var metadata, out var headerError))
            return headerError!;
        var body = await IdentityHttp.ReadBodyAsync<RegisterAccountRequest>(httpContext, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var request = body.Value!;
        var result = await handler.HandleAsync(new Application.RegisterAccount.Command(request.Email, request.Password, request.DisplayName, metadata!), cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value, statusCode: StatusCodes.Status201Created)
            : IdentityHttp.Error(result.Error!, metadata!.CorrelationId);
    }

    private static async Task<IResult> RequestEmailVerificationAsync(
        HttpContext httpContext,
        Application.RequestEmailVerification.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!IdentityHttp.TryMetadata(httpContext, true, out var metadata, out var headerError))
            return headerError!;
        var body = await IdentityHttp.ReadBodyAsync<RequestEmailVerificationRequest>(httpContext, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var result = await handler.HandleAsync(new Application.RequestEmailVerification.Command(body.Value!.Email, metadata!), cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value, statusCode: StatusCodes.Status202Accepted)
            : IdentityHttp.Error(result.Error!, metadata!.CorrelationId);
    }

    private static async Task<IResult> VerifyEmailAsync(
        HttpContext httpContext,
        Application.VerifyEmail.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!IdentityHttp.TryMetadata(httpContext, true, out var metadata, out var headerError))
            return headerError!;
        var body = await IdentityHttp.ReadBodyAsync<VerifyEmailRequest>(httpContext, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var request = body.Value!;
        var result = await handler.HandleAsync(new Application.VerifyEmail.Command(request.Email, request.Code, metadata!), cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value, statusCode: StatusCodes.Status200OK)
            : IdentityHttp.Error(result.Error!, metadata!.CorrelationId);
    }

    private static async Task<IResult> SignInAsync(
        HttpContext httpContext,
        Application.SignIn.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!IdentityHttp.TryMetadata(httpContext, true, out var metadata, out var headerError))
            return headerError!;
        var body = await IdentityHttp.ReadBodyAsync<SignInRequest>(httpContext, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        var request = body.Value!;
        var result = await handler.HandleAsync(new Application.SignIn.Command(request.Email, request.Password, request.DeviceLabel, metadata!), cancellationToken);
        if (!result.IsSuccess)
            return IdentityHttp.Error(result.Error!, metadata!.CorrelationId);
        AppendRefreshCookie(httpContext, result.Value!.RefreshToken);
        return Results.Json(result.Value.Response, statusCode: StatusCodes.Status200OK);
    }

    private static async Task<IResult> GetCurrentSessionAsync(
        HttpContext httpContext,
        Application.GetCurrentSession.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!IdentityHttp.TryMetadata(httpContext, false, out var metadata, out var headerError))
            return headerError!;
        if (!TryPrincipal(httpContext, out var accountId, out var sessionId))
            return IdentityHttp.Error(IdentityErrors.SessionInvalid(), metadata!.CorrelationId);
        var result = await handler.HandleAsync(new Application.GetCurrentSession.Query(accountId, sessionId, metadata!.CorrelationId), cancellationToken);
        return result.IsSuccess ? Results.Json(result.Value) : IdentityHttp.Error(result.Error!, metadata.CorrelationId);
    }

    private static async Task<IResult> RefreshSessionAsync(
        HttpContext httpContext,
        Application.RefreshSession.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!IdentityHttp.TryMetadata(httpContext, true, out var metadata, out var headerError))
            return headerError!;
        var body = await IdentityHttp.ReadBodyAsync<RefreshSessionRequest>(httpContext, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        if (!httpContext.Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken) || string.IsNullOrEmpty(refreshToken))
            return IdentityHttp.Error(IdentityErrors.SessionInvalid(), metadata!.CorrelationId);
        var result = await handler.HandleAsync(new Application.RefreshSession.Command(refreshToken, metadata!), cancellationToken);
        if (!result.IsSuccess)
            return IdentityHttp.Error(result.Error!, metadata!.CorrelationId);
        AppendRefreshCookie(httpContext, result.Value!.RefreshToken);
        return Results.Json(result.Value.Response);
    }

    private static async Task<IResult> SignOutAsync(
        HttpContext httpContext,
        Application.SignOut.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!IdentityHttp.TryMetadata(httpContext, true, out var metadata, out var headerError))
            return headerError!;
        var body = await IdentityHttp.ReadBodyAsync<SignOutRequest>(httpContext, cancellationToken);
        if (body.Error is not null)
            return body.Error;
        if (!TryPrincipal(httpContext, out var accountId, out var sessionId))
            return IdentityHttp.Error(IdentityErrors.SessionInvalid(), metadata!.CorrelationId);
        var result = await handler.HandleAsync(new Application.SignOut.Command(accountId, sessionId, body.Value!.Reason, metadata!), cancellationToken);
        if (!result.IsSuccess)
            return IdentityHttp.Error(result.Error!, metadata!.CorrelationId);
        httpContext.Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions(httpContext));
        return Results.Json(result.Value);
    }

    private static bool TryPrincipal(HttpContext context, out string accountId, out string sessionId)
    {
        accountId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? string.Empty;
        sessionId = context.User.FindFirst("sid")?.Value ?? string.Empty;
        return accountId.Length != 0 && sessionId.Length != 0;
    }

    private static void AppendRefreshCookie(HttpContext context, string refreshToken) =>
        context.Response.Cookies.Append(RefreshCookieName, refreshToken, RefreshCookieOptions(context));

    private static CookieOptions RefreshCookieOptions(HttpContext context) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true
    };
}

internal static class IdentityHttp
{
    internal static bool TryMetadata(HttpContext context, bool requireIdempotency, out RequestMetadata? metadata, out IResult? error)
    {
        metadata = null;
        error = null;
        var requestId = context.Request.Headers["X-Request-Id"].ToString();
        var correlationId = CorrelationId(context);
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (requestId.Length is < 8 or > 128)
            fields["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."];
        var suppliedCorrelation = context.Request.Headers["X-Correlation-Id"].ToString();
        if (suppliedCorrelation.Length != 0 && suppliedCorrelation.Length is < 8 or > 128)
            fields["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."];
        if (requireIdempotency && idempotencyKey.Length is < 8 or > 128)
            fields["Idempotency-Key"] = ["Idempotency-Key must contain between 8 and 128 characters."];
        if (fields.Count != 0)
        {
            error = Error(IdentityErrors.Validation(fields), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        var userAgent = context.Request.Headers.UserAgent.ToString();
        metadata = new RequestMetadata(requestId, correlationId, idempotencyKey, userAgent.Length switch { 0 => null, > 512 => userAgent[..512], _ => userAgent });
        return true;
    }

    internal static string CorrelationId(HttpContext context)
    {
        var supplied = context.Request.Headers["X-Correlation-Id"].ToString();
        return supplied.Length is >= 8 and <= 128 ? supplied : context.TraceIdentifier;
    }

    internal static async Task<BodyRead<T>> ReadBodyAsync<T>(HttpContext context, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(cancellationToken);
            return value is null
                ? new BodyRead<T>(null, Error(IdentityErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["A JSON request body is required."] }), CorrelationId(context)))
                : new BodyRead<T>(value, null);
        }
        catch (JsonException)
        {
            return new BodyRead<T>(null, Error(IdentityErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["The JSON request body does not match the contract."] }), CorrelationId(context)));
        }
        catch (NotSupportedException)
        {
            return new BodyRead<T>(null, Error(IdentityErrors.Validation(new Dictionary<string, string[]> { ["body"] = ["A JSON request body is required."] }), CorrelationId(context)));
        }
    }

    internal static IResult Error(OperationError error, string correlationId) =>
        Results.Json(Problem(error.Code, error.Status, error.Title, correlationId, error.Retryable, error.Detail, error.FieldErrors), statusCode: error.Status, contentType: "application/problem+json");

    internal static IdentityProblemDetails Problem(
        string code,
        int status,
        string title,
        string correlationId,
        bool retryable = false,
        string? detail = null,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new($"urn:unicore:error:{code.ToLowerInvariant()}", title, status, code, retryable, correlationId, detail, null, fieldErrors);

    internal sealed record BodyRead<T>(T? Value, IResult? Error) where T : class;
}
