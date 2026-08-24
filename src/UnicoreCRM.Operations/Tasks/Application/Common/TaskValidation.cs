using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;

namespace UnicoreCRM.Operations.Tasks.Application.Common;

internal sealed record NormalizedCreateTask(
    string Title,
    string? Description,
    TaskPriority Priority,
    string AssigneeId,
    DateTimeOffset DueAt,
    TaskReferenceData References,
    string? DedupeKey);

internal sealed record NormalizedActivity(
    ActivityType Type,
    string Subject,
    string? Body,
    TaskReferenceData References);

internal static partial class TaskValidation
{
    internal static bool TryCreate(
        CreateTaskRequest request,
        out NormalizedCreateTask? value,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var title = Text(request.Title, "title", 1, 300, true, fields);
        var description = Text(request.Description, "description", 0, 4000, false, fields);
        var assigneeId = Entity(request.AssigneeId, "assigneeId", true, fields);
        var dueAt = Utc(request.DueAt, "dueAt", true, fields);
        var priority = Priority(request.Priority, fields);
        var references = References(request.RelationshipRef, request.RecordRef, request.SourceRef, fields);
        var dedupeKey = Text(request.DedupeKey, "dedupeKey", 8, 256, false, fields);
        errors = fields;
        value = fields.Count == 0
            ? new NormalizedCreateTask(title!, description, priority, assigneeId!, dueAt!.Value, references!, dedupeKey)
            : null;
        return value is not null;
    }

    internal static bool TryActivity(
        LogActivityRequest request,
        out NormalizedActivity? value,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var type = Activity(request.Type, fields);
        var subject = Text(request.Subject, "subject", 1, 300, true, fields);
        var body = Text(request.Body, "body", 0, 10000, false, fields);
        var references = References(request.RelationshipRef, request.RecordRef, request.SourceRef, fields);
        errors = fields;
        value = fields.Count == 0 ? new NormalizedActivity(type, subject!, body, references!) : null;
        return value is not null;
    }

    internal static string? RequiredText(string? input, string field, int max, out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var value = Text(input, field, 1, max, true, fields);
        errors = fields;
        return value;
    }

    internal static string? RequiredEntity(string? input, string field, out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var value = Entity(input, field, true, fields);
        errors = fields;
        return value;
    }

    internal static DateTimeOffset? RequiredUtc(string? input, string field, out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var value = Utc(input, field, true, fields);
        errors = fields;
        return value;
    }

    internal static bool IsEntityId(string? value) => value is not null && EntityIdPattern().IsMatch(value);

    internal static bool TryOptionalUtc(string? input, string field, IDictionary<string, string[]> fields, out DateTimeOffset? value)
    {
        value = Utc(input, field, false, fields);
        return !fields.ContainsKey(field);
    }

    internal static bool TryCursor(string? cursor, IDictionary<string, string[]> fields, out int offset)
    {
        offset = 0;
        if (string.IsNullOrEmpty(cursor))
            return true;
        if (cursor.Length > 512)
        {
            fields["cursor"] = ["cursor must contain at most 512 characters."];
            return false;
        }
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(cursor);
            if (bytes.Length != sizeof(int))
                throw new FormatException();
            offset = BitConverter.ToInt32(bytes);
            if (offset < 0)
                throw new FormatException();
            return true;
        }
        catch (FormatException)
        {
            fields["cursor"] = ["cursor is invalid."];
            return false;
        }
    }

    internal static string Cursor(int offset) => WebEncoders.Base64UrlEncode(BitConverter.GetBytes(offset));

    private static string? Text(
        string? input,
        string field,
        int minimum,
        int maximum,
        bool required,
        IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        var value = input.Trim();
        if (value.Length < minimum || value.Length > maximum)
            fields[field] = [$"{field} must contain between {minimum} and {maximum} characters."];
        return value.Length == 0 && !required ? null : value;
    }

    private static string? Entity(
        string? input,
        string field,
        bool required,
        IDictionary<string, string[]> fields)
    {
        var value = Text(input, field, 1, 128, required, fields);
        if (value is not null && !EntityIdPattern().IsMatch(value))
            fields[field] = [$"{field} is not a valid entity identifier."];
        return value;
    }

    private static DateTimeOffset? Utc(
        string? input,
        string field,
        bool required,
        IDictionary<string, string[]> fields)
    {
        if (string.IsNullOrEmpty(input))
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        if (!input.EndsWith('Z')
            || !DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
        {
            fields[field] = [$"{field} must be a UTC date-time ending in Z."];
            return null;
        }
        return value.ToUniversalTime();
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

    private static ActivityType Activity(string? input, IDictionary<string, string[]> fields) => input switch
    {
        "CALL" => ActivityType.Call,
        "EMAIL" => ActivityType.Email,
        "MEETING" => ActivityType.Meeting,
        "NOTE" => ActivityType.Note,
        "MESSAGE" => ActivityType.Message,
        "SYSTEM" => ActivityType.System,
        _ => InvalidActivity(fields)
    };

    private static ActivityType InvalidActivity(IDictionary<string, string[]> fields)
    {
        fields["type"] = ["type must be CALL, EMAIL, MEETING, NOTE, MESSAGE, or SYSTEM."];
        return ActivityType.Note;
    }

    private static TaskReferenceData? References(
        BuyerReference? relationship,
        RecordReference? record,
        TaskSourceReference? source,
        IDictionary<string, string[]> fields)
    {
        string? relationshipType = null;
        string? relationshipId = null;
        if (relationship is not null)
        {
            relationshipType = relationship.Type?.Trim();
            if (relationshipType is not ("CONTACT" or "ORGANIZATION_ACCOUNT"))
                fields["relationshipRef.type"] = ["relationshipRef.type must be CONTACT or ORGANIZATION_ACCOUNT."];
            relationshipId = Entity(relationship.Id, "relationshipRef.id", true, fields);
        }

        string? moduleKey = null;
        string? recordId = null;
        string? label = null;
        if (record is not null)
        {
            moduleKey = Text(record.ModuleKey, "recordRef.moduleKey", 1, 100, true, fields);
            recordId = Entity(record.RecordId, "recordRef.recordId", true, fields);
            label = Text(record.Label, "recordRef.label", 0, 300, false, fields);
        }

        string? sourceType = null;
        string? sourceId = null;
        string? evidence = null;
        if (source is not null)
        {
            sourceType = Text(source.Type, "sourceRef.type", 1, 100, true, fields);
            sourceId = Entity(source.Id, "sourceRef.id", true, fields);
            evidence = Text(source.Evidence, "sourceRef.evidence", 0, 1000, false, fields);
        }

        return new TaskReferenceData(
            relationshipType,
            relationshipId,
            moduleKey,
            recordId,
            label,
            sourceType,
            sourceId,
            evidence);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
