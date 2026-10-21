using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;

namespace UnicoreCRM.Operations.Tasks.Application.LogActivity;

internal sealed record NormalizedActivity(
    ActivityType Type,
    string Subject,
    string? Body,
    TaskReferenceData References);

internal static class LogActivityValidation
{
    internal static bool TryActivity(
        LogActivityRequest request,
        out NormalizedActivity? value,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var type = Activity(request.Type, fields);
        var subject = TaskValidation.Text(request.Subject, "subject", 1, 300, true, fields);
        var body = TaskValidation.Text(request.Body, "body", 0, 10000, false, fields);
        var references = TaskValidation.References(request.RelationshipRef, request.RecordRef, request.SourceRef, fields);
        if (references is not null)
        {
            var module = references.RecordModuleKey ?? ActivityRecordAccess.RelationshipModule(references.RelationshipType);
            var id = references.RecordId ?? references.RelationshipId;
            if (module is not ("leads" or "contacts" or "organizations" or "customers") || id is null)
                fields["recordRef"] = ["Activity requires a supported CRM record or Contact/Organization relationship."];
            var relatedModule = ActivityRecordAccess.RelationshipModule(references.RelationshipType);
            if (relatedModule == module && references.RelationshipId != id)
                fields["relationshipRef"] = ["References to the same module must identify the same record."];
            // HTTP sourceRef is only a UI origin marker, never trusted audit evidence.
            if (!ActivityRecordAccess.ValidSource(references, module, id))
                fields["sourceRef"] = ["sourceRef must be a matching CRM Activity origin without audit evidence."];
        }
        errors = fields;
        value = fields.Count == 0 ? new NormalizedActivity(type, subject!, body, references!) : null;
        return value is not null;
    }

    private static ActivityType Activity(string? input, IDictionary<string, string[]> fields) => input switch
    {
        "CALL" => ActivityType.Call,
        "EMAIL" => ActivityType.Email,
        "MEETING" => ActivityType.Meeting,
        "NOTE" => ActivityType.Note,
        "MESSAGE" => ActivityType.Message,
        _ => InvalidActivity(fields)
    };

    private static ActivityType InvalidActivity(IDictionary<string, string[]> fields)
    {
        fields["type"] = ["type must be CALL, EMAIL, MEETING, NOTE, or MESSAGE. SYSTEM is reserved for trusted system events."];
        return ActivityType.Note;
    }
}
