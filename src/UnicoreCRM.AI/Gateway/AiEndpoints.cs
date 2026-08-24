using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.AI.Gateway;

public static class AiEndpoints
{
    private const long MaximumBodyBytes = 16_384;
    private static readonly JsonSerializerOptions RequestJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/ai/advisories", RequestAdvisoryAsync)
            .RequireAuthorization()
            .RequireTrustedWorkspace()
            .WithName("requestAiAdvisory");
        return endpoints;
    }

    private static async Task<IResult> RequestAdvisoryAsync(
        HttpContext context,
        AiAdvisoryApplication application,
        CancellationToken cancellationToken)
    {
        var correlationId = CorrelationId(context);
        if (!context.Request.HasJsonContentType())
            return Error(AiErrors.UnsupportedMediaType(), correlationId);
        if (context.Request.ContentLength > MaximumBodyBytes)
            return Error(AiErrors.TooLarge(), correlationId);

        AiAdvisoryRequest? request;
        try
        {
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            while (true)
            {
                var read = await context.Request.Body.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;
                if (body.Length + read > MaximumBodyBytes)
                    return Error(AiErrors.TooLarge(), correlationId);
                await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            body.Position = 0;
            request = await JsonSerializer.DeserializeAsync<AiAdvisoryRequest>(body, RequestJsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return Error(AiErrors.Malformed(), correlationId);
        }
        if (request is null)
            return Error(AiErrors.Malformed(), correlationId);

        var result = await application.HandleAsync(request, correlationId, cancellationToken);
        return result.IsSuccess
            ? Results.Json(result.Value)
            : Error(result.Error!, correlationId);
    }

    private static IResult Error(AiOperationError error, string correlationId) =>
        Results.Json(
            new AiProblemDetails(
                $"urn:unicore:error:{error.Code.ToLowerInvariant()}",
                error.Title,
                error.Status,
                error.Code,
                error.Retryable,
                correlationId,
                FieldErrors: error.FieldErrors),
            statusCode: error.Status,
            contentType: "application/problem+json");

    private static string CorrelationId(HttpContext context)
    {
        var supplied = context.Request.Headers["X-Correlation-Id"].ToString();
        return supplied.Length is >= 8 and <= 128 ? supplied : context.TraceIdentifier;
    }
}
