using UnicoreCRM.Crm.Contacts.Domain;

namespace UnicoreCRM.Crm.Contacts.Application.ListContacts;

internal sealed record ContactListFilters(
    string? Search = null, string? Status = null, string? OwnerId = null,
    string? OwnerScope = null, string? Source = null, string? RelationshipLevel = null,
    string? DecisionRole = null, bool? DoNotContact = null, string? Link = null,
    string Sort = "recentlyUpdated", string? NextFollowUpDate = null, string? FollowUp = null);

internal sealed record ContactListSpecification(
    string WorkspaceId, string? ScopeOwnerId, ContactListFilters Filters,
    IReadOnlyList<string> SearchFields, DateTimeOffset? CursorUpdatedAt = null,
    string? CursorName = null, string? CursorId = null,
    UnicoreCRM.Operations.Tasks.Contracts.ContactFollowUpReadAuthority? FollowUpAuthority = null,
    DateTimeOffset? DayStart = null, DateTimeOffset? DayEnd = null,
    DateTimeOffset? CursorFollowUpAt = null, bool CursorFollowUpIsNull = false);

internal sealed record ContactPageRow(Contact Contact, DateTimeOffset? NextFollowUpAt);
internal sealed record ContactListSlice(IReadOnlyList<ContactPageRow> Items, long TotalCount);
