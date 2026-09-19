namespace UnicoreCRM.AI.Providers;

internal enum AiProviderFailure
{
    Unavailable, Timeout, RateLimited, NetworkFailure, AuthenticationFailure,
    InvalidRequest, InvalidResponse, SafetyRefusal, Cancelled
}

internal sealed class AiProviderExecutionException(AiProviderFailure failure, string safeDiagnostic, Exception? inner = null)
    : Exception(safeDiagnostic, inner)
{
    internal AiProviderFailure Failure { get; } = failure;
    internal string SafeDiagnostic { get; } = safeDiagnostic;
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
    internal static AiProviderExecutionException FromStatus(System.Net.HttpStatusCode status) => (int)status switch
    {
        400 or 404 or 422 => new(AiProviderFailure.InvalidRequest, "Provider rejected the admitted request."),
        401 or 403 => new(AiProviderFailure.AuthenticationFailure, "Provider authentication failed."),
        429 => new(AiProviderFailure.RateLimited, "Provider rate limit exceeded."),
        >= 500 => new(AiProviderFailure.Unavailable, "Provider service is unavailable."),
        _ => new(AiProviderFailure.InvalidResponse, "Provider returned an unexpected HTTP status.")
    };
}
