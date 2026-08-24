namespace UnicoreCRM.Platform.Workspace.Domain;

internal sealed class WorkspaceDefinition
{
    private WorkspaceDefinition() { }

    internal WorkspaceDefinition(string key, string name, string logoText, DateTimeOffset now)
    {
        WorkspaceId = WorkspaceIds.New("ws");
        Key = key;
        Name = name;
        LogoText = logoText;
        CreatedAt = now;
    }

    public string WorkspaceId { get; private set; } = null!;
    public string Key { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string LogoText { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}
