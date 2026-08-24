using System.Globalization;
using UnicoreCRM.AI.Context;
using UnicoreCRM.Operations.Tasks.Contracts;

namespace UnicoreCRM.AI.Tools;

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
