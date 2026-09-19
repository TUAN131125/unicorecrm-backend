using System.Text.Json.Serialization;

namespace UnicoreCRM.AI.Gateway;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiContextReference(string? Type, string? Id);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiConversationMessage(string? Role, string? Content);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiAdvisoryRequest(
    string? Question,
    string? Locale,
    IReadOnlyList<AiContextReference>? ContextReferences,
    IReadOnlyList<AiConversationMessage>? Conversation = null);

public sealed record AiAdvisoryProviderView(string Name, string Model);

public sealed record AiGroundingEvidence(string EntityType, string EntityId, string? DisplayLabel, long? Version, string ContextType);

public sealed record AiAdvisoryResponse(
    string ExecutionId,
    string Summary,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SuggestedNextAction,
    IReadOnlyList<string> AttentionPoints,
    bool Advisory,
    IReadOnlyList<AiContextReference> ContextReferences,
    IReadOnlyList<AiGroundingEvidence> Evidence,
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
