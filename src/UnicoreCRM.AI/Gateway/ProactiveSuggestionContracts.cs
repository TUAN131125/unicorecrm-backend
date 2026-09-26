using System.Text.Json.Serialization;

namespace UnicoreCRM.AI.Gateway;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProactiveSuggestionRequest(string? Locale = null);
public sealed record ProactiveSuggestionWhy(string ReasonCode, string Explanation);
public sealed record ProactiveTaskDraft(string Title, string Description);
public sealed record ProactiveSuggestionResponse(string ExecutionId, string ItemId, string CustomerId,
    string Summary, ProactiveSuggestionWhy Why, string SuggestedNextStep, ProactiveTaskDraft TaskDraft);
