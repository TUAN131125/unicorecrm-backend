namespace UnicoreCRM.Platform.AccessControl.Domain;

internal sealed class AccessRole
{
    private AccessRole() { }

    internal AccessRole(string workspaceId, string name, string? description, string? sourceTemplateId, DateTimeOffset now)
    {
        RoleId = AccessControlIds.New("role");
        WorkspaceId = workspaceId;
        Name = name;
        Description = description;
        SourceTemplateId = sourceTemplateId;
        IsActive = true;
        Version = 0;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string RoleId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? SourceTemplateId { get; private set; }
    public bool IsActive { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
