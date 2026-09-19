using UnicoreCRM.AI.Context;

namespace UnicoreCRM.AI.Tools;

internal interface IAiContextTool
{
    string Name { get; }
    string EntityType { get; }

    Task<AiContextToolResult> ExecuteAsync(
        string referenceId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken);
}
