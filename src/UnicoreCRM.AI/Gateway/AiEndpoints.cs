using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;
using System.Globalization;
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
        endpoints.MapGet("/ai/configuration/catalog", GetCatalogAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("getAiProviderCatalog");
        endpoints.MapGet("/ai/configuration", GetConfigurationAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("getAiConfiguration");
        endpoints.MapGet("/ai/configuration/usage", GetUsageAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("getAiUsageSummary");
        endpoints.MapPut("/ai/configuration", SaveConfigurationAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("saveAiConfiguration");
        endpoints.MapPut("/ai/configuration/credential", SetCredentialAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("setAiCredential");
        endpoints.MapPost("/ai/configuration/test", TestConfigurationAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("testAiConfiguration");
        endpoints.MapPost("/ai/configuration/activate", ActivateConfigurationAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("activateAiConfiguration");
        endpoints.MapPost("/ai/configuration/disable", DisableConfigurationAsync).RequireAuthorization().RequireTrustedWorkspace().WithName("disableAiConfiguration");
        return endpoints;
    }

    private static async Task<IResult> GetCatalogAsync(HttpContext context, AiConfigurationApplication application, CancellationToken cancellationToken)
        => Result(await application.CatalogAsync(CorrelationId(context), cancellationToken), CorrelationId(context));

    private static async Task<IResult> GetConfigurationAsync(HttpContext context, AiConfigurationApplication application, CancellationToken cancellationToken)
    {
        var result = await application.GetAsync(CorrelationId(context), cancellationToken);
        if (result.IsSuccess) context.Response.Headers.ETag = $"\"{result.Value!.Version}\"";
        return Result(result, CorrelationId(context));
    }

    private static async Task<IResult> GetUsageAsync(HttpContext context, AiConfigurationApplication application, CancellationToken cancellationToken)
        => Result(await application.UsageAsync(CorrelationId(context), cancellationToken), CorrelationId(context));

    private static async Task<IResult> SaveConfigurationAsync(SaveAiConfigurationRequest request, HttpContext context, AiConfigurationApplication application, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var expectedVersion, out var idempotencyKey, out var error)) return error!;
        var result = await application.SaveAsync(request, expectedVersion, idempotencyKey!, CorrelationId(context), cancellationToken);
        if (result.IsSuccess) context.Response.Headers.ETag = $"\"{result.Value!.Configuration.Version}\"";
        return Result(result, CorrelationId(context));
    }

    private static async Task<IResult> SetCredentialAsync(SetAiCredentialRequest request, HttpContext context, AiConfigurationApplication application, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var expectedVersion, out var idempotencyKey, out var error)) return error!;
        var result = await application.SetCredentialAsync(request, expectedVersion, idempotencyKey!, CorrelationId(context), cancellationToken);
        if (result.IsSuccess) context.Response.Headers.ETag = $"\"{result.Value!.Configuration.Version}\"";
        return Result(result, CorrelationId(context));
    }

    private static async Task<IResult> TestConfigurationAsync(EmptyCommandRequest _, HttpContext context, AiConfigurationApplication application, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var expectedVersion, out var idempotencyKey, out var error)) return error!;
        var result = await application.TestAsync(expectedVersion, idempotencyKey!, CorrelationId(context), cancellationToken);
        if (result.IsSuccess) context.Response.Headers.ETag = $"\"{result.Value!.Configuration.Version}\"";
        return Result(result, CorrelationId(context));
    }

    private static async Task<IResult> ActivateConfigurationAsync(EmptyCommandRequest _, HttpContext context, AiConfigurationApplication application, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var expectedVersion, out var idempotencyKey, out var error)) return error!;
        var result = await application.ActivateAsync(expectedVersion, idempotencyKey!, CorrelationId(context), cancellationToken);
        if (result.IsSuccess) context.Response.Headers.ETag = $"\"{result.Value!.Configuration.Version}\"";
        return Result(result, CorrelationId(context));
    }

    private static async Task<IResult> DisableConfigurationAsync(EmptyCommandRequest _, HttpContext context, AiConfigurationApplication application, CancellationToken cancellationToken)
    {
        if (!TryCommandMetadata(context, out var expectedVersion, out var idempotencyKey, out var error)) return error!;
        var result = await application.DisableAsync(expectedVersion, idempotencyKey!, CorrelationId(context), cancellationToken);
        if (result.IsSuccess) context.Response.Headers.ETag = $"\"{result.Value!.Configuration.Version}\"";
        return Result(result, CorrelationId(context));
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

    private static IResult Result<T>(AiOperationResult<T> result, string correlationId) =>
        result.IsSuccess ? Results.Json(result.Value) : Error(result.Error!, correlationId);

    private static bool TryCommandMetadata(HttpContext context, out long expectedVersion, out string? idempotencyKey, out IResult? error)
    {
        expectedVersion = -1; idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString(); error = null;
        var ifMatch = context.Request.Headers["If-Match"].ToString();
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (idempotencyKey.Length is < 8 or > 128) fields["Idempotency-Key"] = ["Idempotency-Key must contain between 8 and 128 characters."];
        if (ifMatch.Length < 3 || ifMatch[0] != '"' || ifMatch[^1] != '"' ||
            !long.TryParse(ifMatch[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out expectedVersion) || expectedVersion < 0)
            fields["If-Match"] = ["If-Match must contain a quoted non-negative resource version."];
        if (fields.Count == 0) return true;
        error = Error(AiErrors.Invalid(fields), CorrelationId(context)); return false;
    }

    private static string CorrelationId(HttpContext context)
    {
        var supplied = context.Request.Headers["X-Correlation-Id"].ToString();
        return supplied.Length is >= 8 and <= 128 ? supplied : context.TraceIdentifier;
    }
}
