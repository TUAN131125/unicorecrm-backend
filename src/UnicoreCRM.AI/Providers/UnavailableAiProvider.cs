namespace UnicoreCRM.AI.Providers;

internal sealed class UnavailableAiProvider : IAiProvider
{
    public AiProviderDescriptor Descriptor { get; } = new("unavailable", "not-configured");

    public Task<AiProviderResponse> CompleteAsync(
        AiProviderRequest request,
        CancellationToken cancellationToken) =>
        throw new AiProviderUnavailableException();
}
