using UnicoreCRM.AI.Gateway;
using UnicoreCRM.AI.Tools;

namespace UnicoreCRM.AI.Context;

internal sealed class AiContextComposer(AiToolRegistry toolRegistry)
{
    internal async Task<AiContextCompositionResult> LoadAsync(
        AiAdvisoryContextReferences references,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var requests = new List<(string ToolName, string ReferenceId)>(3);
        Add(requests, LeadSummaryTool.ToolName, references.LeadId);
        Add(requests, DealSummaryTool.ToolName, references.DealId);
        Add(requests, TaskSummaryTool.ToolName, references.TaskId);

        var items = new List<AiContextItem>(requests.Count);
        var usedTools = new List<string>(requests.Count);
        foreach (var request in requests)
        {
            var result = await toolRegistry.ExecuteAsync(
                request.ToolName,
                request.ReferenceId,
                requestId,
                correlationId,
                cancellationToken);
            usedTools.Add(request.ToolName);
            if (result.Status != AiContextLoadStatus.Succeeded)
                return new([], usedTools, Error(result.Status));
            items.Add(result.Item!);
        }

        return new(items, usedTools, null);
    }

    private static void Add(
        ICollection<(string ToolName, string ReferenceId)> requests,
        string toolName,
        string? referenceId)
    {
        if (!string.IsNullOrWhiteSpace(referenceId))
            requests.Add((toolName, referenceId));
    }

    private static AiOperationError Error(AiContextLoadStatus status) => status switch
    {
        AiContextLoadStatus.AccessDenied => AiErrors.AccessDenied(),
        AiContextLoadStatus.WorkspaceMismatch => AiErrors.WorkspaceMismatch(),
        AiContextLoadStatus.InvalidReference => AiErrors.Invalid(
            new Dictionary<string, string[]> { ["contextReferences"] = ["A context reference is invalid."] }),
        AiContextLoadStatus.NotFound => AiErrors.ContextNotFound(),
        _ => AiErrors.Invalid(
            new Dictionary<string, string[]> { ["contextReferences"] = ["A context tool request was rejected."] })
    };
}
