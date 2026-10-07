namespace UnicoreCRM.Operations.Tasks.Contracts;

/// <summary>
/// Tasks-owned SQL view contract. Map this keyless row with ToView(ViewName, Schema).
/// The view is unprivileged: consumers MUST obtain authority and call Compose before
/// joining Contacts, counting, filtering or paging. Contacts authorization is additional.
/// Each row is the earliest open, unarchived contacts RecordRef task for one assignee.
/// RelationshipRef alone does not target a Contact. No Task type is inferred.
/// </summary>
public sealed class ContactFollowUpProjectionRow
{
    public const string Schema = "tasks";
    public const string ViewName = "ContactFollowUpReadProjection";
    public string WorkspaceId { get; set; } = null!;
    public string ContactId { get; set; } = null!;
    public string AssigneeId { get; set; } = null!;
    public DateTimeOffset MinDueAt { get; set; }
}

public sealed class ContactNextFollowUp
{
    public string WorkspaceId { get; set; } = null!;
    public string ContactId { get; set; } = null!;
    public DateTimeOffset NextFollowUpAt { get; set; }
}

/// <summary>Request-local authority issued only by Tasks after canonical tasks.read authorization.</summary>
public sealed class ContactFollowUpReadAuthority
{
    internal ContactFollowUpReadAuthority(string workspaceId, string? scopeOwnedByMember)
    {
        WorkspaceId = workspaceId;
        ScopeOwnedByMember = scopeOwnedByMember;
    }

    public string WorkspaceId { get; }
    public string? ScopeOwnedByMember { get; }

    /// <summary>
    /// Apply trusted workspace and assignee scope BEFORE reducing to a per-Contact minimum.
    /// Remains IQueryable for SQL composition. Left join to obtain null when no eligible task exists.
    /// Input must be the mapped Tasks-owned view, never a foreign persistence table.
    /// </summary>
    public IQueryable<ContactNextFollowUp> Compose(IQueryable<ContactFollowUpProjectionRow> view)
    {
        var eligible = view.Where(row => row.WorkspaceId == WorkspaceId);
        if (ScopeOwnedByMember is { } memberId)
            eligible = eligible.Where(row => row.AssigneeId == memberId);
        return eligible.GroupBy(row => new { row.WorkspaceId, row.ContactId })
            .Select(group => new ContactNextFollowUp
            {
                WorkspaceId = group.Key.WorkspaceId,
                ContactId = group.Key.ContactId,
                NextFollowUpAt = group.Min(row => row.MinDueAt)
            });
    }
}

public sealed record ContactFollowUpAuthorityResult(ContactFollowUpReadAuthority? Authority, string? ErrorCode)
{
    public bool IsAvailable => Authority is not null;
}

public interface IContactFollowUpReadAuthorityResolver
{
    Task<ContactFollowUpAuthorityResult> ResolveAsync(
        string requestId, string correlationId, CancellationToken cancellationToken);
}
