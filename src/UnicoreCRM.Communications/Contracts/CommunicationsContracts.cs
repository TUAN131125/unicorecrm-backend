using System.Text.Json.Serialization;

namespace UnicoreCRM.Communications.Contracts;

public sealed record ConversationClaimDocument(string ConversationId, long Version, string AuditId,
    DateTimeOffset OccurredAt, bool Replayed, string CorrelationId);

public sealed record ConversationDocument(string Id, DateTimeOffset LastActivityAt, long Version, string Status, bool CanTransition);
public sealed record ConversationTransitionDocument(string Id, string Status, long Version, bool Changed,
    string? AuditId, DateTimeOffset OccurredAt, string CorrelationId);
public sealed record QueueConversationDocument(string Id, DateTimeOffset LastActivityAt);
public sealed record CrmLinkTargetDocument(string TargetType, string TargetId, string? Label, bool Eligible);
public sealed record ConversationCrmContextDocument(string State, long Version, bool CanLink,
    CrmLinkTargetDocument? Target, CrmLinkTargetDocument? Successor);
public sealed record ConversationLinkMutationDocument(string Id, long Version, bool Changed, string? AuditId,
    DateTimeOffset OccurredAt, string CorrelationId);
public sealed record ConversationMessageDocument(string Id, string ConversationId, long Sequence,
    DateTimeOffset OccurredAt, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text);
public sealed record CommunicationsPage<T>(IReadOnlyList<T> Items, string? NextCursor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? CanClaim = null);
