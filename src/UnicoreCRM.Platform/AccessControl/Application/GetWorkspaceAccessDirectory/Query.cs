namespace UnicoreCRM.Platform.AccessControl.Application.GetWorkspaceAccessDirectory;

internal sealed record Query(
    string RequestId,
    string CorrelationId,
    string SuppliedCorrelationId);
