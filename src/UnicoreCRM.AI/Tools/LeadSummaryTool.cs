using System.Globalization;
using UnicoreCRM.AI.Context;
using UnicoreCRM.Crm.Leads.Contracts;

namespace UnicoreCRM.AI.Tools;

internal sealed class LeadSummaryTool(ILeadSummaryReader reader) : IAiContextTool
{
    internal const string ToolName = "lead.summary.read";
    public string Name => ToolName;

    public async Task<AiContextToolResult> ExecuteAsync(
        string referenceId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var result = await reader.ReadAsync(referenceId, requestId, correlationId, cancellationToken);
        if (result.Status != LeadSummaryReadStatus.Succeeded)
            return new(Map(result.Status));

        var summary = result.Summary!;
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Add(fields, "displayName", summary.DisplayName);
        Add(fields, "workState", summary.WorkState);
        Add(fields, "score", summary.Score?.ToString(CultureInfo.InvariantCulture));
        Add(fields, "priority", summary.Priority);
        Add(fields, "nextFollowUpAt", summary.NextFollowUpAt);
        return new(AiContextLoadStatus.Succeeded, new AiContextItem("lead", summary.LeadId, fields));
    }

    private static AiContextLoadStatus Map(LeadSummaryReadStatus status) => status switch
    {
        LeadSummaryReadStatus.AccessDenied => AiContextLoadStatus.AccessDenied,
        LeadSummaryReadStatus.WorkspaceMismatch => AiContextLoadStatus.WorkspaceMismatch,
        LeadSummaryReadStatus.InvalidReference => AiContextLoadStatus.InvalidReference,
        LeadSummaryReadStatus.NotFound => AiContextLoadStatus.NotFound,
        _ => AiContextLoadStatus.ToolRejected
    };

    private static void Add(IDictionary<string, string> fields, string key, string? value)
    {
        if (value is not null)
            fields.Add(key, value);
    }
}
