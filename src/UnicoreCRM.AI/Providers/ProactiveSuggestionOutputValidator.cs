using System.Text.Json;
using UnicoreCRM.AI.Gateway;

namespace UnicoreCRM.AI.Providers;

internal sealed record ProactiveSuggestionOutput(string Summary, string SuggestedNextStep, ProactiveTaskDraft TaskDraft);

internal static class ProactiveSuggestionOutputValidator
{
    // Compatible with Tasks/Application/CreateTask/CreateTaskValidation.cs.
    internal const int TitleMaximum = 300;
    internal const int DescriptionMaximum = 4000;

    internal static readonly JsonElement Schema = JsonSerializer.Deserialize<JsonElement>("""
        {"type":"object","additionalProperties":false,"required":["summary","suggestedNextStep","taskDraft"],"properties":{
          "summary":{"type":"string"},"suggestedNextStep":{"type":"string"},
          "taskDraft":{"type":"object","additionalProperties":false,"required":["title","description"],"properties":{
            "title":{"type":"string"},"description":{"type":"string"}}}}}
        """);

    internal static ProactiveSuggestionOutput? Validate(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 65536) return null;
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (!ExactProperties(root, "summary", "suggestedNextStep", "taskDraft")) return null;
            var draft = root.GetProperty("taskDraft");
            if (!ExactProperties(draft, "title", "description")) return null;
            var summary = Text(root.GetProperty("summary"), 2000);
            var step = Text(root.GetProperty("suggestedNextStep"), 2000);
            var title = Text(draft.GetProperty("title"), TitleMaximum);
            var description = Text(draft.GetProperty("description"), DescriptionMaximum);
            return summary is null || step is null || title is null || description is null
                ? null : new(summary, step, new(title, description));
        }
        catch (JsonException) { return null; }
    }

    private static bool ExactProperties(JsonElement element, params string[] names) =>
        element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static string? Text(JsonElement element, int maximum)
    {
        if (element.ValueKind != JsonValueKind.String) return null;
        var value = element.GetString()!.Trim();
        return value.Length is 0 || value.Length > maximum ? null : value;
    }
}
