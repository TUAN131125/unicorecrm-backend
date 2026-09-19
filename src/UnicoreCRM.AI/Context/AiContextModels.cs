namespace UnicoreCRM.AI.Context;

internal sealed record AiContextItem(
    string EntityType,
    string EntityId,
    IReadOnlyDictionary<string, string> Fields,
    string? DisplayLabel = null,
    long? Version = null,
    string? ContextType = null);

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
