namespace UnicoreCRM.AI.Providers;

internal enum AiProviderFailure
{
    Unavailable, Timeout, RateLimited, NetworkFailure, AuthenticationFailure,
    InvalidRequest, InvalidResponse, SafetyRefusal, Cancelled, PermissionDenied, ModelUnavailable, BillingRequired
}

internal enum AiProviderTimeoutSource { ApplicationDeadline, HttpClientDeadline }

internal sealed class AiProviderExecutionException(AiProviderFailure failure, string safeDiagnostic, Exception? inner = null)
    : Exception(safeDiagnostic, inner)
{
    internal AiProviderFailure Failure { get; } = failure;
    internal string SafeDiagnostic { get; } = safeDiagnostic;
    internal int? ProviderHttpStatus { get; init; }
    internal string? ProviderErrorCode { get; init; }
    internal AiProviderTimeoutSource? TimeoutSource { get; init; }
    internal HttpRequestError? NetworkError { get; init; }
}

internal sealed record AiProviderAdapterResult(
    string Provider, string Model, string Content, TimeSpan Duration,
    string? ProviderRequestId = null, int? InputTokens = null, int? OutputTokens = null,
    string? SafeDiagnostic = null);

internal interface IProductionAiProviderAdapter
{
    string ProviderId { get; }
    Task<AiProviderAdapterResult> CompleteAsync(string model, string credential, AiProviderRequest request, CancellationToken cancellationToken);
}

internal static class ProviderHttpFailureMapper
{
    internal static AiProviderExecutionException FromStatus(System.Net.HttpStatusCode status) => Classify(status).WithStatus(status);

    private static AiProviderExecutionException WithStatus(this AiProviderExecutionException error, System.Net.HttpStatusCode status)
        => new(error.Failure, error.SafeDiagnostic) { ProviderHttpStatus = (int)status };

    private static AiProviderExecutionException Classify(System.Net.HttpStatusCode status) => (int)status switch
    {
        400 or 422 => new(AiProviderFailure.InvalidRequest, "Provider rejected the admitted request."),
        401 => new(AiProviderFailure.AuthenticationFailure, "Provider authentication failed."),
        402 => new(AiProviderFailure.BillingRequired, "Provider billing or credits are required."),
        403 => new(AiProviderFailure.PermissionDenied, "Provider denied access to the resource."),
        404 => new(AiProviderFailure.ModelUnavailable, "Provider model or endpoint is unavailable."),
        408 or 504 => new(AiProviderFailure.Timeout, "Provider request timed out."),
        429 => new(AiProviderFailure.RateLimited, "Provider rate limit exceeded."),
        >= 500 => new(AiProviderFailure.Unavailable, "Provider service is unavailable."),
        _ => new(AiProviderFailure.InvalidResponse, "Provider returned an unexpected HTTP status.")
    };

    internal static async Task<AiProviderExecutionException> FromGeminiAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var fallback = FromStatus(response.StatusCode);
        // Only HTTP 400 needs ErrorInfo to distinguish authentication from request errors.
        // Do not let a stalled error body hide a conclusive 401/403/429/5xx status as a timeout.
        if (response.StatusCode != System.Net.HttpStatusCode.BadRequest) return fallback;
        // Bound untrusted error input. Never retain provider messages, metadata, URLs or keys.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[16385];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken);
            if (read == 0) break;
            count += read;
        }
        if (count == buffer.Length) return fallback;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(buffer.AsMemory(0, count));
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object || !root.TryGetProperty("error", out var error)
                || error.ValueKind != System.Text.Json.JsonValueKind.Object) return fallback;
            var safeCode = error.TryGetProperty("status", out var status) && status.ValueKind == System.Text.Json.JsonValueKind.String
                ? status.GetString() switch
                {
                    "INVALID_ARGUMENT" => "INVALID_ARGUMENT", "FAILED_PRECONDITION" => "FAILED_PRECONDITION",
                    "UNAUTHENTICATED" => "UNAUTHENTICATED", "PERMISSION_DENIED" => "PERMISSION_DENIED",
                    "NOT_FOUND" => "NOT_FOUND", "RESOURCE_EXHAUSTED" => "RESOURCE_EXHAUSTED",
                    "INTERNAL" => "INTERNAL", "UNAVAILABLE" => "UNAVAILABLE", "DEADLINE_EXCEEDED" => "DEADLINE_EXCEEDED",
                    _ => null
                } : null;
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest
                && error.TryGetProperty("details", out var details) && details.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var detail in details.EnumerateArray())
                    if (detail.ValueKind == System.Text.Json.JsonValueKind.Object
                        && detail.TryGetProperty("@type", out var type) && type.ValueKind == System.Text.Json.JsonValueKind.String
                        && type.GetString() == "type.googleapis.com/google.rpc.ErrorInfo"
                        && detail.TryGetProperty("reason", out var reason) && reason.ValueKind == System.Text.Json.JsonValueKind.String
                        && reason.GetString() is "API_KEY_INVALID" or "API_KEY_EXPIRED")
                        return new(AiProviderFailure.AuthenticationFailure, "Provider authentication failed.")
                        { ProviderHttpStatus = (int)response.StatusCode, ProviderErrorCode = reason.GetString() == "API_KEY_INVALID" ? "API_KEY_INVALID" : "API_KEY_EXPIRED" };
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest
                && safeCode == "FAILED_PRECONDITION")
                return new(AiProviderFailure.BillingRequired, "Provider account prerequisites are not satisfied.")
                { ProviderHttpStatus = (int)response.StatusCode, ProviderErrorCode = safeCode };
            return new(fallback.Failure, fallback.SafeDiagnostic) { ProviderHttpStatus = (int)response.StatusCode, ProviderErrorCode = safeCode };
        }
        catch (System.Text.Json.JsonException) { return fallback; }
    }
}
