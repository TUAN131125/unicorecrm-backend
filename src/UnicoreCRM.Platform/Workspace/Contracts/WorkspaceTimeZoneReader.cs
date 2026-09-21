namespace UnicoreCRM.Platform.Workspace.Contracts;

/// <summary>Workspace-owned system projection for UTC/local-day scheduling.</summary>
public interface IWorkspaceTimeZoneReader
{
    Task<string?> ReadTimeZoneAsync(string workspaceId, CancellationToken cancellationToken);
}
