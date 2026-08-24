using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using Domain = UnicoreCRM.Operations.Tasks.Domain;

namespace UnicoreCRM.Operations.Tasks.Application.Common;

internal static class TaskProjection
{
    internal static TaskReadModel Task(TaskItem item) => new(
        item.TaskId,
        item.Title,
        item.Status switch { Domain.TaskStatus.Completed => "COMPLETED", Domain.TaskStatus.Cancelled => "CANCELLED", _ => "OPEN" },
        item.Priority switch { TaskPriority.Low => "LOW", TaskPriority.High => "HIGH", TaskPriority.Urgent => "URGENT", _ => "NORMAL" },
        item.AssigneeId,
        Utc(item.DueAt),
        Utc(item.CreatedAt),
        Utc(item.UpdatedAt),
        item.Version,
        item.Description,
        OptionalUtc(item.CompletedAt),
        OptionalUtc(item.CancelledAt),
        item.CancellationReason,
        item.Outcome,
        Buyer(item.RelationshipType, item.RelationshipId),
        Record(item.RecordModuleKey, item.RecordId, item.RecordLabel),
        Source(item.SourceType, item.SourceId, item.SourceEvidence),
        OptionalUtc(item.ArchivedAt),
        item.ArchiveReason);

    internal static ActivityReadModel Activity(TaskActivity item) => new(
        item.ActivityId,
        item.Type switch
        {
            ActivityType.Call => "CALL",
            ActivityType.Email => "EMAIL",
            ActivityType.Meeting => "MEETING",
            ActivityType.Message => "MESSAGE",
            ActivityType.System => "SYSTEM",
            _ => "NOTE"
        },
        item.Subject,
        item.ActorId,
        Utc(item.OccurredAt),
        item.Body,
        Buyer(item.RelationshipType, item.RelationshipId),
        Record(item.RecordModuleKey, item.RecordId, item.RecordLabel),
        Source(item.SourceType, item.SourceId, item.SourceEvidence));

    internal static string Utc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static string? OptionalUtc(DateTimeOffset? value) => value is null ? null : Utc(value.Value);
    private static BuyerReference? Buyer(string? type, string? id) => type is null || id is null ? null : new(type, id);
    private static RecordReference? Record(string? moduleKey, string? id, string? label) =>
        moduleKey is null || id is null ? null : new(moduleKey, id, label);
    private static TaskSourceReference? Source(string? type, string? id, string? evidence) =>
        type is null || id is null ? null : new(type, id, evidence);
}
