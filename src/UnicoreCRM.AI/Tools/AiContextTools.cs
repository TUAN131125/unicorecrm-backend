using System.Globalization;
using UnicoreCRM.AI.Context;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Operations.Tasks.Contracts;

namespace UnicoreCRM.AI.Tools;

internal interface IAiContextTool
{
    string Name { get; }

    Task<AiContextToolResult> ExecuteAsync(
        string referenceId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken);
}

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

internal sealed class TaskSummaryTool(ITaskSummaryReader reader) : IAiContextTool
{
    internal const string ToolName = "task.summary.read";
    public string Name => ToolName;

    public async Task<AiContextToolResult> ExecuteAsync(
        string referenceId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var result = await reader.ReadAsync(referenceId, requestId, correlationId, cancellationToken);
        if (result.Status != TaskSummaryReadStatus.Succeeded)
            return new(Map(result.Status));

        var summary = result.Summary!;
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Add(fields, "title", summary.Title);
        Add(fields, "status", summary.Status);
        Add(fields, "priority", summary.Priority);
        Add(fields, "dueAt", summary.DueAt);
        return new(AiContextLoadStatus.Succeeded, new AiContextItem("task", summary.TaskId, fields));
    }

    private static AiContextLoadStatus Map(TaskSummaryReadStatus status) => status switch
    {
        TaskSummaryReadStatus.AccessDenied => AiContextLoadStatus.AccessDenied,
        TaskSummaryReadStatus.WorkspaceMismatch => AiContextLoadStatus.WorkspaceMismatch,
        TaskSummaryReadStatus.InvalidReference => AiContextLoadStatus.InvalidReference,
        TaskSummaryReadStatus.NotFound => AiContextLoadStatus.NotFound,
        _ => AiContextLoadStatus.ToolRejected
    };

    private static void Add(IDictionary<string, string> fields, string key, string? value)
    {
        if (value is not null)
            fields.Add(key, value);
    }
}
