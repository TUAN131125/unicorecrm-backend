namespace UnicoreCRM.AI.Context;

internal sealed record AiContextItem(
    string EntityType,
    string EntityId,
    IReadOnlyDictionary<string, string> Fields);

internal enum AiContextLoadStatus
{
    Succeeded,
    AccessDenied,
    WorkspaceMismatch,
    InvalidReference,
    NotFound,
    ToolRejected
}

internal sealed record AiContextToolResult(
    AiContextLoadStatus Status,
    AiContextItem? Item = null);

internal sealed record AiContextCompositionResult(
    IReadOnlyList<AiContextItem> Items,
    IReadOnlyList<string> ToolNames,
    Gateway.AiOperationError? Error)
{
    internal bool IsSuccess => Error is null;
}
