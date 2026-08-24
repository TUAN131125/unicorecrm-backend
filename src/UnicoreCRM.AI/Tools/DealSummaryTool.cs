using System.Globalization;
using UnicoreCRM.AI.Context;
using UnicoreCRM.Crm.Deals.Contracts;

namespace UnicoreCRM.AI.Tools;

internal sealed class DealSummaryTool(IDealSummaryReader reader) : IAiContextTool
{
    internal const string ToolName = "deal.summary.read";
    public string Name => ToolName;

    public async Task<AiContextToolResult> ExecuteAsync(
        string referenceId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var result = await reader.ReadAsync(referenceId, requestId, correlationId, cancellationToken);
        if (result.Status != DealSummaryReadStatus.Succeeded)
            return new(Map(result.Status));

        var summary = result.Summary!;
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Add(fields, "name", summary.Name);
        Add(fields, "stageCode", summary.StageCode);
        Add(fields, "stageCategory", summary.StageCategory);
        Add(fields, "opportunityScore", summary.OpportunityScore);
        Add(fields, "expectedCloseDate", summary.ExpectedCloseDate);
        Add(fields, "nextActionAt", summary.NextActionAt);
        Add(fields, "nextActionSummary", summary.NextActionSummary);
        return new(AiContextLoadStatus.Succeeded, new AiContextItem("deal", summary.DealId, fields));
    }

    private static AiContextLoadStatus Map(DealSummaryReadStatus status) => status switch
    {
        DealSummaryReadStatus.AccessDenied => AiContextLoadStatus.AccessDenied,
        DealSummaryReadStatus.WorkspaceMismatch => AiContextLoadStatus.WorkspaceMismatch,
        DealSummaryReadStatus.InvalidReference => AiContextLoadStatus.InvalidReference,
        DealSummaryReadStatus.NotFound => AiContextLoadStatus.NotFound,
        _ => AiContextLoadStatus.ToolRejected
    };

    private static void Add(IDictionary<string, string> fields, string key, string? value)
    {
        if (value is not null)
            fields.Add(key, value);
    }
}
