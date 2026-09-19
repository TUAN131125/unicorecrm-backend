using UnicoreCRM.AI.Gateway;
using UnicoreCRM.AI.Tools;

namespace UnicoreCRM.AI.Context;

internal sealed class AiContextComposer(AiToolRegistry toolRegistry)
{
    internal async Task<AiContextCompositionResult> LoadAsync(
        IReadOnlyList<AiContextReference> references,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var requests = references.Select(reference => (EntityType: reference.Type!, ReferenceId: reference.Id!)).ToArray();

        var items = new List<AiContextItem>(requests.Length);
        var usedTools = new List<string>(requests.Length);
        foreach (var request in requests)
        {
            var result = await toolRegistry.ExecuteAsync(
                request.EntityType,
                request.ReferenceId,
                requestId,
                correlationId,
                cancellationToken);
            usedTools.Add(result.Item?.ContextType ?? request.EntityType);
            if (result.Status != AiContextLoadStatus.Succeeded)
                return new([], usedTools, Error(result.Status));
            items.Add(result.Item!);
        }

        return new(items, usedTools, null);
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
