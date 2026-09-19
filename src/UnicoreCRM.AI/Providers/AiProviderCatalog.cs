using Microsoft.Extensions.Configuration;
using UnicoreCRM.AI.Gateway;

namespace UnicoreCRM.AI.Providers;

internal sealed class AiProviderCatalog(IConfiguration configuration)
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<AiProviderModelView>> Models =
        new Dictionary<string, IReadOnlyList<AiProviderModelView>>(StringComparer.Ordinal)
        {
            ["GEMINI"] = [new("gemini-2.5-flash", "Gemini 2.5 Flash", true), new("gemini-2.5-pro", "Gemini 2.5 Pro", true)],
            ["OPENAI"] = [new("gpt-5-mini", "GPT-5 mini", true), new("gpt-5", "GPT-5", true)]
        };

    internal bool IsAdmitted(string provider, string model) =>
        Models.TryGetValue(provider, out var models) && models.Any(x => x.Id == model);

    internal AiProviderCatalogResponse View() => new(Models.Select(pair => new AiProviderCatalogEntry(
        pair.Key, pair.Key == "GEMINI" ? "Google Gemini" : "OpenAI",
        !string.IsNullOrWhiteSpace(configuration[$"AI:Providers:{pair.Key}:ApiKey"]), pair.Value)).ToArray());
}
