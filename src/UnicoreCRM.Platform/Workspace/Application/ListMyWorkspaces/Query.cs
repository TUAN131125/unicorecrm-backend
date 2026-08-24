namespace UnicoreCRM.Platform.Workspace.Application.ListMyWorkspaces;

internal sealed record Query(string AccountId, string MemberId, string CorrelationId);
