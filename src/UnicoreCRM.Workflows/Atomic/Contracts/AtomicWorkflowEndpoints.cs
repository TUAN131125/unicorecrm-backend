using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Workflows.Atomic.Application.Common;

namespace UnicoreCRM.Workflows.Atomic.Contracts;

public static class AtomicWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapAtomicWorkflowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The single provisioning intent. It is deliberately not workspace-required: no trusted
        // Workspace can exist for an account that holds zero Workspace memberships.
        endpoints.MapPost("/workspaces/initial-provisioning", ProvisionInitialWorkspaceAsync)
            .RequireAuthorization()
            .WithName("provisionInitialWorkspace");
        return endpoints;
    }

    private static async Task<IResult> ProvisionInitialWorkspaceAsync(
        HttpContext context,
        Application.ProvisionInitialWorkspace.Handler handler,
        CancellationToken cancellationToken)
    {
        if (!AtomicWorkflowHttp.TryMetadata(context, out var metadata, out var metadataError))
            return metadataError!;
        if (!TryPrincipal(context, out var accountId, out var memberId))
            return AtomicWorkflowHttp.Error(AtomicWorkflowErrors.AuthenticationRequired(), metadata!.CorrelationId);

        var body = await AtomicWorkflowHttp.ReadBodyAsync<ProvisionInitialWorkspaceRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;

        var result = await handler.HandleAsync(
            new Application.ProvisionInitialWorkspace.Command(accountId, memberId, body.Value!, metadata),
            cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value, statusCode: result.SuccessStatus)
            : AtomicWorkflowHttp.Error(result.Error!, metadata.CorrelationId);
    }

    private static bool TryPrincipal(HttpContext context, out string accountId, out string memberId)
    {
        accountId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? string.Empty;
        memberId = context.User.FindFirst("member_id")?.Value ?? string.Empty;
        return accountId.Length != 0 && memberId.Length != 0;
    }
}

internal static class AtomicWorkflowHttp
{
    internal static bool TryMetadata(HttpContext context, out AtomicWorkflowMetadata? metadata, out IResult? error)
    {
        metadata = null;
        error = null;
        var requestId = context.Request.Headers["X-Request-Id"].ToString();
        var suppliedCorrelation = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelation.Length is >= 8 and <= 128 ? suppliedCorrelation : context.TraceIdentifier;
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (requestId.Length is < 8 or > 128)
            fields["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."];
        if (suppliedCorrelation.Length != 0 && suppliedCorrelation.Length is < 8 or > 128)
            fields["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."];
        if (idempotencyKey.Length is < 8 or > 128)
            fields["Idempotency-Key"] = ["Idempotency-Key must contain between 8 and 128 characters."];
        if (fields.Count != 0)
        {
            error = Error(AtomicWorkflowErrors.Validation(fields), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new AtomicWorkflowMetadata(requestId, correlationId, idempotencyKey);
        return true;
    }

    internal static async Task<BodyRead<T>> ReadBodyAsync<T>(HttpContext context, string correlationId, CancellationToken cancellationToken)
        where T : class, new()
    {
        // An omitted body is the explicit Skip path and resolves to the server-owned defaults.
        if (context.Request.ContentLength is null or 0)
            return new(new T(), null);
        try
        {
            var value = await context.Request.ReadFromJsonAsync<T>(cancellationToken);
            return value is null ? new(new T(), null) : new(value, null);
        }
        catch (JsonException)
        {
            return new(null, BodyError("The JSON request body does not match the contract.", correlationId));
        }
        catch (NotSupportedException)
        {
            return new(null, BodyError("The JSON request body does not match the contract.", correlationId));
        }
    }

    internal static IResult Error(AtomicWorkflowError error, string correlationId) =>
        Results.Json(
            new AtomicWorkflowProblemDetails(
                $"urn:unicore:error:{error.Code.ToLowerInvariant()}",
                error.Title,
                error.Status,
                error.Code,
                false,
                correlationId,
                error.Detail,
                null,
                error.FieldErrors,
                error.IdempotencyKey),
            statusCode: error.Status,
            contentType: "application/problem+json");

    private static IResult BodyError(string message, string correlationId) =>
        Error(AtomicWorkflowErrors.Validation(new Dictionary<string, string[]> { ["body"] = [message] }), correlationId);

    internal sealed record BodyRead<T>(T? Value, IResult? Error) where T : class;
}
