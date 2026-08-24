using System.Text.Json.Serialization;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Operations.Tasks.Contracts;

public static class TaskCapabilities
{
    public static AccessRequirement Read { get; } = AccessRequirement.ForCanonicalCapability("tasks.read");
    public static AccessRequirement Create { get; } = AccessRequirement.ForCanonicalCapability("tasks.create");
    public static AccessRequirement Update { get; } = AccessRequirement.ForCanonicalCapability("tasks.update");
    public static AccessRequirement Assign { get; } = AccessRequirement.ForCanonicalCapability("tasks.assign");
    public static AccessRequirement Complete { get; } = AccessRequirement.ForCanonicalCapability("tasks.complete");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateTaskRequest(
    string? Title,
    string? AssigneeId,
    string? DueAt,
    string? Description = null,
    string? Priority = null,
    BuyerReference? RelationshipRef = null,
    RecordReference? RecordRef = null,
    TaskSourceReference? SourceRef = null,
    string? DedupeKey = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssignTaskRequest(string? AssigneeId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompleteTaskRequest(string? Outcome);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelTaskRequest(string? Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArchiveTaskRequest(string? Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RescheduleTaskRequest(string? DueAt);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LogActivityRequest(
    string? Type,
    string? Subject,
    string? Body = null,
    BuyerReference? RelationshipRef = null,
    RecordReference? RecordRef = null,
    TaskSourceReference? SourceRef = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BuyerReference(string? Type, string? Id);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordReference(string? ModuleKey, string? RecordId, string? Label = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TaskSourceReference(string? Type, string? Id, string? Evidence = null);

public sealed record TaskReadModel(
    string Id,
    string Title,
    string Status,
    string Priority,
    string AssigneeId,
    string DueAt,
    string CreatedAt,
    string UpdatedAt,
    long ResourceVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CompletedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CancelledAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CancellationReason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Outcome = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BuyerReference? RelationshipRef = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RecordReference? RecordRef = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskSourceReference? SourceRef = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ArchivedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ArchiveReason = null);

public sealed record ActivityReadModel(
    string Id,
    string Type,
    string Subject,
    string ActorId,
    string OccurredAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BuyerReference? RelationshipRef = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RecordReference? RecordRef = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TaskSourceReference? SourceRef = null);

public sealed record TaskMutationResult(TaskReadModel Task);
public sealed record ActivityMutationResult(ActivityReadModel Activity);

public sealed record TaskMutationResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    string OccurredAt,
    string Outcome,
    TaskMutationResult Result,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,
    IReadOnlyList<string> AuditEvidenceIds);

public sealed record ActivityMutationResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    string OccurredAt,
    string Outcome,
    ActivityMutationResult Result,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,
    IReadOnlyList<string> AuditEvidenceIds);

public sealed record TaskListResponse(IReadOnlyList<TaskReadModel> Items, PageInfo PageInfo);
public sealed record ActivityListResponse(IReadOnlyList<ActivityReadModel> Items, PageInfo PageInfo);

public sealed record PageInfo(
    bool HasNextPage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalCount = null);

public sealed record TaskProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Instance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AggregateId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ExpectedVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? CurrentVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IdempotencyKey = null);
