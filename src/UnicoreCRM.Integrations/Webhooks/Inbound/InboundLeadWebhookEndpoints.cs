using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace UnicoreCRM.Integrations.Webhooks.Inbound;

public static class InboundLeadWebhookEndpoints
{
    private const int MaximumRequestBytes = 65_536;

    public static IEndpointRouteBuilder MapInboundLeadWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/integrations/inbound/leads/{integrationId}", HandleAsync)
            .AllowAnonymous()
            .WithName("receiveGenericSignedLeadWebhook")
            .WithMetadata(new RequestSizeLimitAttribute(MaximumRequestBytes));
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        string integrationId,
        HttpContext context,
        InboundLeadWebhookCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var suppliedCorrelationId = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlationId = suppliedCorrelationId.Length is >= 8 and <= 128
            ? suppliedCorrelationId
            : context.TraceIdentifier;
        context.Response.Headers["X-Correlation-Id"] = correlationId;

        if (!context.Request.HasJsonContentType())
            return Problem(415, "UNSUPPORTED_MEDIA_TYPE", "Content type must be application/json", false, correlationId);
        if (context.Request.ContentLength > MaximumRequestBytes)
            return Problem(413, "PAYLOAD_TOO_LARGE", "Webhook payload is too large", false, correlationId);

        var rawPayload = await ReadBoundedBodyAsync(context.Request.Body, cancellationToken);
        if (rawPayload is null)
            return Problem(413, "PAYLOAD_TOO_LARGE", "Webhook payload is too large", false, correlationId);
        if (rawPayload.Length == 0)
            return Problem(400, "MALFORMED_PAYLOAD", "Webhook payload is malformed", false, correlationId);

        var result = await coordinator.ExecuteAsync(
            new VerifiedWebhookRequest(
                integrationId,
                context.Request.Headers["X-Unicore-Delivery-Id"].ToString(),
                context.Request.Headers["X-Unicore-Timestamp"].ToString(),
                context.Request.Headers["X-Unicore-Signature"].ToString(),
                correlationId,
                rawPayload),
            cancellationToken);
        return result.Receipt is not null
            ? Results.Json(result.Receipt, statusCode: result.Status)
            : Results.Json(result.Problem, statusCode: result.Status, contentType: "application/problem+json");
    }

    private static async Task<byte[]?> ReadBoundedBodyAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                return buffer.ToArray();
            if (buffer.Length + read > MaximumRequestBytes)
                return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }

    private static IResult Problem(
        int status,
        string code,
        string title,
        bool retryable,
        string correlationId) =>
        Results.Json(
            new InboundWebhookProblemDetails(
                $"urn:unicore:error:{code.ToLowerInvariant()}",
                title,
                status,
                code,
                retryable,
                correlationId),
            statusCode: status,
            contentType: "application/problem+json");
}
