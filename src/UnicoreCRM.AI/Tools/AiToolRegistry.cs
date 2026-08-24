using UnicoreCRM.AI.Context;

namespace UnicoreCRM.AI.Tools;

internal sealed class AiToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAiContextTool> tools;

    public AiToolRegistry(IEnumerable<IAiContextTool> tools)
    {
        var registered = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        if (registered.Count is 0 or > 3)
            throw new InvalidOperationException("The AI context tool allowlist must contain between one and three tools.");
        this.tools = registered;
    }

    internal Task<AiContextToolResult> ExecuteAsync(
        string toolName,
        string referenceId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken) =>
        tools.TryGetValue(toolName, out var tool)
            ? tool.ExecuteAsync(referenceId, requestId, correlationId, cancellationToken)
            : Task.FromResult(new AiContextToolResult(AiContextLoadStatus.ToolRejected));
}
