namespace UnicoreCRM.Platform.Workspace.Domain;

internal static class WorkspaceIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
