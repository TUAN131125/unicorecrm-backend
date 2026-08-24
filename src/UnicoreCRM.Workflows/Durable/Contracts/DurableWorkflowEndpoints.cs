using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Workflows.Durable.Application.Common;

namespace UnicoreCRM.Workflows.Durable.Contracts;

public static class DurableWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapDurableWorkflowEndpoints(this IEndpointRouteBuilder endpoints)
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
        if (!DurableWorkflowHttp.TryMetadata(context, out var metadata, out var metadataError))
            return metadataError!;
        if (!TryPrincipal(context, out var accountId, out var memberId))
            return DurableWorkflowHttp.Error(DurableWorkflowErrors.AuthenticationRequired(), metadata!.CorrelationId);

        var body = await DurableWorkflowHttp.ReadBodyAsync<ProvisionInitialWorkspaceRequest>(context, metadata!.CorrelationId, cancellationToken);
        if (body.Error is not null)
            return body.Error;

        var result = await handler.HandleAsync(
            new Application.ProvisionInitialWorkspace.Command(accountId, memberId, body.Value!, metadata),
            cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value, statusCode: result.SuccessStatus)
            : DurableWorkflowHttp.Error(result.Error!, metadata.CorrelationId);
    }

    private static bool TryPrincipal(HttpContext context, out string accountId, out string memberId)
    {
        accountId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? string.Empty;
        memberId = context.User.FindFirst("member_id")?.Value ?? string.Empty;
        return accountId.Length != 0 && memberId.Length != 0;
    }
}

internal static class DurableWorkflowHttp
{
    internal static bool TryMetadata(HttpContext context, out DurableWorkflowMetadata? metadata, out IResult? error)
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
            error = Error(DurableWorkflowErrors.Validation(fields), correlationId);
            return false;
        }

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        metadata = new DurableWorkflowMetadata(requestId, correlationId, idempotencyKey);
        return true;
    }

    /// <summary>The provisioning intent carries a handful of short scalars; anything larger is rejected.</summary>
    internal const int MaximumRequestBodyBytes = 8192;

    /// <summary>
    /// Strict request reading. Unknown members are rejected here rather than relying on ambient
    /// host serializer configuration, and the declared Content-Length is never used as the
    /// emptiness signal, so a chunked body cannot be silently discarded as a Skip.
    /// </summary>
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static async Task<BodyRead<T>> ReadBodyAsync<T>(HttpContext context, string correlationId, CancellationToken cancellationToken)
        where T : class, new()
    {
        var buffer = new byte[MaximumRequestBodyBytes + 1];
        var read = await context.Request.Body.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken);
        if (read > MaximumRequestBodyBytes)
            return new(null, BodyError($"The JSON request body must not exceed {MaximumRequestBodyBytes} bytes.", correlationId));

        // An absent or whitespace-only body is the explicit Skip path.
        var content = new ReadOnlySpan<byte>(buffer, 0, read);
        if (IsBlank(content))
            return new(new T(), null);
        try
        {
            var value = JsonSerializer.Deserialize<T>(content, StrictJson);
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

    private static bool IsBlank(ReadOnlySpan<byte> content)
    {
        foreach (var value in content)
        {
            if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
                return false;
        }
        return true;
    }

    internal static IResult Error(DurableWorkflowError error, string correlationId) =>
        Results.Json(
            new DurableWorkflowProblemDetails(
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
        Error(DurableWorkflowErrors.Validation(new Dictionary<string, string[]> { ["body"] = [message] }), correlationId);

    internal sealed record BodyRead<T>(T? Value, IResult? Error) where T : class;
}
