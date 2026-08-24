using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnicoreCRM.AI.Providers;

internal sealed record ValidatedAiAdvisory(
    string Summary,
    string? SuggestedNextAction,
    IReadOnlyList<string> AttentionPoints);

internal sealed class AiProviderOutputValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal ValidatedAiAdvisory? Validate(string content)
    {
        ProviderAdvisoryPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ProviderAdvisoryPayload>(content, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        var summary = payload?.Summary?.Trim();
        var suggested = payload?.SuggestedNextAction?.Trim();
        var attention = payload?.AttentionPoints;
        if (string.IsNullOrEmpty(summary)
            || summary.Length > 2000
            || suggested?.Length > 1000
            || attention is null
            || attention.Count > 5)
        {
            return null;
        }

        var normalizedAttention = new List<string>(attention.Count);
        foreach (var item in attention)
        {
            var normalized = item?.Trim();
            if (string.IsNullOrEmpty(normalized) || normalized.Length > 500)
                return null;
            normalizedAttention.Add(normalized);
        }

        return new ValidatedAiAdvisory(
            summary,
            string.IsNullOrEmpty(suggested) ? null : suggested,
            normalizedAttention);
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ProviderAdvisoryPayload(
        string? Summary,
        string? SuggestedNextAction,
        IReadOnlyList<string?>? AttentionPoints);
}
