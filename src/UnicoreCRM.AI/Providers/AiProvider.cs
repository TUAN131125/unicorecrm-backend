namespace UnicoreCRM.AI.Providers;

internal sealed record AiProviderDescriptor(string Name, string Model);

internal enum AiProviderOutputContract { Advisory, ProactiveSuggestion }

internal sealed record AiProviderRequest(
    string ExecutionId,
    string SystemInstruction,
    string UserInstruction,
    string ContextData,
    string Locale,
    int ContextCount,
    AiProviderOutputContract OutputContract = AiProviderOutputContract.Advisory);

internal sealed record AiProviderResponse(string Content, string? RequestId = null, int? InputTokens = null, int? OutputTokens = null,
    string? Provider = null, string? Model = null);

internal interface IAiProvider
{
    AiProviderDescriptor Descriptor { get; }

    Task<AiProviderResponse> CompleteAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken);
}

internal sealed class AiProviderUnavailableException : Exception
{
    internal AiProviderUnavailableException() : base("The configured AI provider is unavailable.") { }
}

internal sealed class AiProviderRateLimitedException : Exception
{
    internal AiProviderRateLimitedException() : base("The configured AI provider rate limit was exceeded.") { }
}
internal sealed class AiProviderInvalidResponseException : Exception
{
    internal AiProviderInvalidResponseException() : base("The configured AI provider returned an invalid response.") { }
}
internal sealed class AiProviderSafetyRefusalException : Exception
{
    internal AiProviderSafetyRefusalException() : base("The configured AI provider refused the request under its safety policy.") { }
}

internal sealed record AiProviderRuntimeOptions(TimeSpan Timeout);
