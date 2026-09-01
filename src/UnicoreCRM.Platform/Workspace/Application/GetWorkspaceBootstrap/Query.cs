namespace UnicoreCRM.Platform.Workspace.Application.GetWorkspaceBootstrap;

internal sealed record Query(string AccountId, string MemberId, string WorkspaceId, string RequestId, string CorrelationId);
