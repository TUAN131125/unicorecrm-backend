using System.Text.Json.Serialization;

namespace UnicoreCRM.AI.Gateway;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiAdvisoryContextReferences(
    string? LeadId = null,
    string? DealId = null,
    string? TaskId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiAdvisoryRequest(
    string? Question,
    string? Locale,
    AiAdvisoryContextReferences? ContextReferences);

public sealed record AiAdvisoryProviderView(string Name, string Model);

public sealed record AiAdvisoryResponse(
    string ExecutionId,
    string Summary,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SuggestedNextAction,
    IReadOnlyList<string> AttentionPoints,
    bool Advisory,
    AiAdvisoryContextReferences ContextReferences,
    AiAdvisoryProviderView Provider);

public sealed record AiProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? FieldErrors = null);
