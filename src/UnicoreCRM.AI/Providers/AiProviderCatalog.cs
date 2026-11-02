using Microsoft.Extensions.Configuration;
using UnicoreCRM.AI.Gateway;

namespace UnicoreCRM.AI.Providers;

internal sealed class AiProviderCatalog(IConfiguration configuration)
{
    internal const string DefaultOpenAiModel = "gpt-6-luna";
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<AiProviderModelView>> Models =
        new Dictionary<string, IReadOnlyList<AiProviderModelView>>(StringComparer.Ordinal)
        {
            ["GEMINI"] = [new("gemini-3.8-flash", "Gemini 3.8 Flash", true), new("gemini-2.5-flash", "Gemini 2.5 Flash", true), new("gemini-2.5-pro", "Gemini 2.5 Pro", true)],
            ["OPENAI"] =
            [
                new(DefaultOpenAiModel, "GPT-6 Luna", true),
                new("gpt-6.1-sol", "GPT-6.1 Sol", true),
                new("gpt-6-astra", "GPT-6 Astra", true),
                new("gpt-5.6-sol", "GPT-5.6 Sol", true),
                new("gpt-6-sol", "GPT-6 Sol", true),
                new("gpt-5.6-terra", "GPT-5.6 Terra", true),
                new("gpt-5.6-luna", "GPT-5.6 Luna", true)
            ]
        };

    internal bool IsAdmitted(string provider, string model) =>
        Models.TryGetValue(provider, out var models) && (models.Any(x => x.Id == model)
            // Compatibility for persisted policies. Do not silently replace a saved model.
            || provider == "OPENAI" && (model is "gpt-5" or "gpt-5-mini"));

    internal static bool IsCurrentOpenAiModel(string model) => Models["OPENAI"].Any(x => x.Id == model);

    internal AiProviderCatalogResponse View() => new(Models.Select(pair => new AiProviderCatalogEntry(
        pair.Key, pair.Key == "GEMINI" ? "Google Gemini" : "OpenAI",
        !string.IsNullOrWhiteSpace(configuration[$"AI:Providers:{pair.Key}:ApiKey"]), pair.Value)).ToArray());
}
