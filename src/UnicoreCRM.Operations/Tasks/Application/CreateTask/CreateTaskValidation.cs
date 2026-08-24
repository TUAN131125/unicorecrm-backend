using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;

namespace UnicoreCRM.Operations.Tasks.Application.CreateTask;

internal sealed record NormalizedCreateTask(
    string Title,
    string? Description,
    TaskPriority Priority,
    string AssigneeId,
    DateTimeOffset DueAt,
    TaskReferenceData References,
    string? DedupeKey);

internal static class CreateTaskValidation
{
    internal static bool TryCreate(
        CreateTaskRequest request,
        out NormalizedCreateTask? value,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var title = TaskValidation.Text(request.Title, "title", 1, 300, true, fields);
        var description = TaskValidation.Text(request.Description, "description", 0, 4000, false, fields);
        var assigneeId = TaskValidation.Entity(request.AssigneeId, "assigneeId", true, fields);
        var dueAt = TaskValidation.Utc(request.DueAt, "dueAt", true, fields);
        var priority = Priority(request.Priority, fields);
        var references = TaskValidation.References(request.RelationshipRef, request.RecordRef, request.SourceRef, fields);
        var dedupeKey = TaskValidation.Text(request.DedupeKey, "dedupeKey", 8, 256, false, fields);
        errors = fields;
        value = fields.Count == 0
            ? new NormalizedCreateTask(title!, description, priority, assigneeId!, dueAt!.Value, references!, dedupeKey)
            : null;
        return value is not null;
    }

    private static TaskPriority Priority(string? input, IDictionary<string, string[]> fields)
    {
        if (input is null)
            return TaskPriority.Normal;
        return input switch
        {
            "LOW" => TaskPriority.Low,
            "NORMAL" => TaskPriority.Normal,
            "HIGH" => TaskPriority.High,
            "URGENT" => TaskPriority.Urgent,
            _ => InvalidPriority(fields)
        };
    }

    private static TaskPriority InvalidPriority(IDictionary<string, string[]> fields)
    {
        fields["priority"] = ["priority must be LOW, NORMAL, HIGH, or URGENT."];
        return TaskPriority.Normal;
    }
}
