namespace UnicoreCRM.Platform.AccessControl.Domain;

internal sealed class AccessRole
{
    private AccessRole() { }

    internal AccessRole(string workspaceId, string name, string? description, string? sourceTemplateId, DateTimeOffset now)
    {
        RoleId = AccessControlIds.New("role");
        WorkspaceId = workspaceId;
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
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
    public string NormalizedName { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? SourceTemplateId { get; private set; }
    public bool IsActive { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// The full replacement of the role's mutable scalar configuration under
    /// <c>DEC-REPLACEACCESSROLE-AUTHORITY-CLOSURE</c>. The identity, owning Workspace, creation
    /// instant and active state are invariant here: <c>replaceAccessRole</c> performs no lifecycle
    /// transition, which <c>archiveAccessRole</c> owns exclusively. Omitted optional scalars arrive
    /// as null and clear the stored value; there is no preserve-on-omission behavior.
    /// </summary>
    /// <summary>
    /// The lifecycle deactivation owned exclusively by <c>archiveAccessRole</c> under
    /// <c>DEC-ARCHIVEACCESSROLE-AUTHORITY-CLOSURE</c>. Archive is not deletion: the row, its name,
    /// description, template provenance, capabilities, policies and assignments all survive, and an
    /// inactive role simply contributes no effective authority. No reactivation transition is
    /// admitted by any operation, so there is deliberately no inverse of this method.
    /// </summary>
    internal void Archive(DateTimeOffset now)
    {
        IsActive = false;
        Version = checked(Version + 1);
        UpdatedAt = now;
    }

    internal void Replace(string name, string? description, string? sourceTemplateId, DateTimeOffset now)
    {
        Name = name.Trim();
        NormalizedName = Name.ToUpperInvariant();
        Description = description;
        SourceTemplateId = sourceTemplateId;
        Version = checked(Version + 1);
        UpdatedAt = now;
    }
}
