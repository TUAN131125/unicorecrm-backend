namespace UnicoreCRM.AI.Providers;

internal sealed record AiProviderDescriptor(string Name, string Model);

internal sealed record AiProviderRequest(
    string SystemInstruction,
    string UserInstruction,
    string ContextData,
    string Locale,
    int ContextCount);

internal sealed record AiProviderResponse(string Content);

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

internal sealed record AiProviderRuntimeOptions(TimeSpan Timeout);
