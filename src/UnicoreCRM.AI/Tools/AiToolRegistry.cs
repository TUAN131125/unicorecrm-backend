using UnicoreCRM.AI.Context;

namespace UnicoreCRM.AI.Tools;

internal sealed class AiToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAiContextTool> tools;

    public AiToolRegistry(IEnumerable<IAiContextTool> tools)
    {
        var registered = tools.ToDictionary(tool => tool.EntityType, StringComparer.OrdinalIgnoreCase);
        if (registered.Count is 0 or > 6)
            throw new InvalidOperationException("The AI context tool allowlist must contain between one and six tools.");
        this.tools = registered;
    }

    internal Task<AiContextToolResult> ExecuteAsync(
        string entityType,
        string referenceId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken) =>
        tools.TryGetValue(entityType, out var tool)
            ? tool.ExecuteAsync(referenceId, requestId, correlationId, cancellationToken)
            : Task.FromResult(new AiContextToolResult(AiContextLoadStatus.ToolRejected));
}
