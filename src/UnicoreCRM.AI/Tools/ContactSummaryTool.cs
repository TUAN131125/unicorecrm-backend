using UnicoreCRM.AI.Context;
using UnicoreCRM.Crm.Contacts.Contracts;

namespace UnicoreCRM.AI.Tools;

internal sealed class ContactSummaryTool(IContactSummaryReader reader) : IAiContextTool
{
    internal const string ToolName = "contact.summary.read";
    public string Name => ToolName;
    public string EntityType => "contact";
    public async Task<AiContextToolResult> ExecuteAsync(string id, string requestId, string correlationId, CancellationToken ct)
    {
        var result = await reader.ReadAsync(id, requestId, correlationId, ct);
        if (result.Status != ContactSummaryReadStatus.Succeeded) return new(Map(result.Status));
        var s = result.Summary!; var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Add(fields, "displayName", s.DisplayName); Add(fields, "status", s.Status); Add(fields, "jobTitle", s.JobTitle); Add(fields, "relationshipLevel", s.RelationshipLevel); Add(fields, "needSummary", s.NeedSummary);
        return new(AiContextLoadStatus.Succeeded, new AiContextItem("contact", s.ContactId, fields, s.DisplayName, s.Version, ToolName));
    }
    private static AiContextLoadStatus Map(ContactSummaryReadStatus s) => s switch { ContactSummaryReadStatus.AccessDenied => AiContextLoadStatus.AccessDenied, ContactSummaryReadStatus.WorkspaceMismatch => AiContextLoadStatus.WorkspaceMismatch, ContactSummaryReadStatus.InvalidReference => AiContextLoadStatus.InvalidReference, ContactSummaryReadStatus.NotFound => AiContextLoadStatus.NotFound, _ => AiContextLoadStatus.ToolRejected };
    private static void Add(IDictionary<string,string> fields,string key,string? value) { if (value is not null) fields.Add(key,value); }
}
