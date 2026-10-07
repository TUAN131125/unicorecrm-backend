using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using System.Globalization;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.ListContacts;

internal sealed record Query(ContactRequestMetadata Metadata, ContactListFilters? Filters = null, string? Cursor = null, int? Limit = null);
internal sealed record PageInfo(bool HasNextPage, string? NextCursor, long TotalCount);
internal sealed record ContactListResponse(IReadOnlyList<ContactDocument> Items, PageInfo PageInfo, bool FollowUpAvailable);
internal sealed record ContactListSummary(long TotalCount, IReadOnlyDictionary<string, long> StatusCounts);
internal sealed record ContactCursor(string Scope, string Id, DateTimeOffset UpdatedAt, string Name, DateTimeOffset ExpiresAt, DateTimeOffset? FollowUpAt);

internal sealed class Handler(ContactAuthorization authorization, IContactsPersistence persistence, TimeProvider timeProvider, IDataProtectionProvider protection, IContactFollowUpReadAuthorityResolver followUpResolver, IWorkspaceTimeZoneReader timeZones)
{
    private readonly IDataProtector cursorProtection = protection.CreateProtector("Contacts.List.Keyset.v1");
    internal async Task<ContactOperationResult<ContactListResponse>> HandleAsync(Query query, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(query, cancellationToken);
        if (!prepared.IsSuccess) return ContactOperationResult<ContactListResponse>.Failure(prepared.Error!);
        var (access, specification, scope) = prepared.Value;
        if (access.Authorization.ScopeFilter == RecordAccessScopeFilter.Denied) return ContactOperationResult<ContactListResponse>.Success(new([], new(false, null, 0), false));
        if (query.Cursor is { } cursor)
        {
            try
            {
                var p = JsonSerializer.Deserialize<ContactCursor>(cursorProtection.Unprotect(cursor));
                if (p is null || p.Scope != scope || p.ExpiresAt <= timeProvider.GetUtcNow() || string.IsNullOrEmpty(p.Id)) return InvalidCursor();
                specification = specification with { CursorId = p.Id, CursorUpdatedAt = p.UpdatedAt, CursorName = p.Name, CursorFollowUpAt = p.FollowUpAt, CursorFollowUpIsNull = p.FollowUpAt is null };
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException) { return InvalidCursor(); }
        }
        var limit = query.Limit ?? 25;
        var result = await persistence.ReadContactPageAsync(specification, limit + 1, cancellationToken);
        var items = result.Items.Take(limit).ToArray();
        var hasNext = result.Items.Count > limit;
        var next = hasNext ? cursorProtection.Protect(JsonSerializer.Serialize(new ContactCursor(scope, items[^1].Contact.ContactId, items[^1].Contact.UpdatedAt, items[^1].Contact.FullName, timeProvider.GetUtcNow().AddHours(1), items[^1].NextFollowUpAt))) : null;
        await AuditAsync(access, query.Metadata, "listContacts", cancellationToken);
        return ContactOperationResult<ContactListResponse>.Success(new(items.Select(c => ContactFieldSecurity.Project(ContactProjection.Document(c.Contact), access.Authorization) with { NextFollowUpAt = c.NextFollowUpAt is { } followUpAt ? ContactProjection.TimestampValue(followUpAt) : null }).ToArray(), new(hasNext, next, result.TotalCount), specification.FollowUpAuthority is not null));
    }
    internal async Task<ContactOperationResult<ContactListSummary>> SummaryAsync(Query query, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(query with { Cursor = null }, cancellationToken);
        if (!prepared.IsSuccess) return ContactOperationResult<ContactListSummary>.Failure(prepared.Error!);
        var (access, specification, _) = prepared.Value;
        IReadOnlyDictionary<string, long> counts = access.Authorization.ScopeFilter == RecordAccessScopeFilter.Denied ? new Dictionary<string, long>() : await persistence.ReadContactStatusCountsAsync(specification, cancellationToken);
        await AuditAsync(access, query.Metadata, "getContactListSummary", cancellationToken);
        return ContactOperationResult<ContactListSummary>.Success(new(counts.Values.Sum(), counts));
    }
    private async Task<ContactOperationResult<(ContactAccess Access, ContactListSpecification Specification, string Scope)>> PrepareAsync(Query query, CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess) return ContactOperationResult<(ContactAccess, ContactListSpecification, string)>.Failure(access.Error!);
        var f = query.Filters ?? new();
        f = f with { Search = string.IsNullOrWhiteSpace(f.Search) ? null : f.Search.Trim(), OwnerId = f.OwnerScope == "my" ? access.Value!.Trusted.MemberId : f.OwnerId };
        var errors = new Dictionary<string, string[]>();
        if ((query.Limit ?? 25) is < 1 or > 100) errors["limit"] = ["limit must be between 1 and 100."];
        if (f.Search?.Length > 200) errors["search"] = ["search cannot exceed 200 characters."];
        if (f.Sort is not "recentlyUpdated" and not "nameAsc" and not "nextFollowUp") errors["sort"] = ["The requested sort is unavailable."];
        if (f.FollowUp is not null and not "today" and not "overdue") errors["followUp"] = ["Unknown follow-up view."];
        if (f.NextFollowUpDate is not null && !DateOnly.TryParseExact(f.NextFollowUpDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) errors["nextFollowUpDate"] = ["Invalid business date."];
        if (f.NextFollowUpDate is not null && f.FollowUp is not null) errors["nextFollowUpDate"] = ["Use either a business date or a follow-up view."];
        if (f.OwnerScope is not null and not "my") errors["ownerScope"] = ["Only my owner scope is supported."];
        if (f.Link is not null and not "linked" and not "unlinked") errors["link"] = ["link must be linked or unlinked."];
        if (f.Status is not null && !new[] { "active", "needs_follow_up", "in_consulting", "has_open_opportunity", "inactive", "do_not_contact", "archived" }.Contains(f.Status)) errors["status"] = ["Unknown Contact status."];
        foreach (var (field, value) in new[] { ("ownerId", f.OwnerId), ("source", f.Source), ("relationshipLevel", f.RelationshipLevel), ("decisionRole", f.DecisionRole) })
        {
            if (value is not null && (value.Length == 0 || value.Length > 200)) errors[field] = ["Invalid filter value."];
            if (value is not null && !access.Value!.Authorization.CanRead(field)) return ContactOperationResult<(ContactAccess, ContactListSpecification, string)>.Failure(ContactErrors.AccessDenied());
        }
        if (f.DoNotContact.HasValue && !access.Value!.Authorization.CanRead("doNotContact")) return ContactOperationResult<(ContactAccess, ContactListSpecification, string)>.Failure(ContactErrors.AccessDenied());
        if (errors.Count != 0) return ContactOperationResult<(ContactAccess, ContactListSpecification, string)>.Failure(ContactErrors.Validation(errors));
        var a = access.Value!;
        var followUp = a.Authorization.CanRead("nextFollowUpAt") ? await followUpResolver.ResolveAsync(query.Metadata.RequestId, query.Metadata.CorrelationId, cancellationToken) : new ContactFollowUpAuthorityResult(null, "ACCESS_DENIED");
        if (followUp.Authority is { } taskAccess && taskAccess.WorkspaceId != a.Trusted.WorkspaceId) return ContactOperationResult<(ContactAccess, ContactListSpecification, string)>.Failure(ContactErrors.WorkspaceMismatch());
        if (!followUp.IsAvailable && (f.Sort == "nextFollowUp" || f.NextFollowUpDate is not null || f.FollowUp is not null)) return ContactOperationResult<(ContactAccess, ContactListSpecification, string)>.Failure(ContactErrors.AccessDenied());
        DateTimeOffset? dayStart = null, dayEnd = null;
        if (f.NextFollowUpDate is not null || f.FollowUp is not null)
        {
            var zoneId = await timeZones.ReadTimeZoneAsync(a.Trusted.WorkspaceId, cancellationToken);
            if (zoneId is null) return ContactOperationResult<(ContactAccess, ContactListSpecification, string)>.Failure(new("INTEGRATION_UNAVAILABLE", 503, "Business timezone unavailable"));
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
                var date = f.NextFollowUpDate is { } requestedDate ? DateOnly.ParseExact(requestedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture) : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone).DateTime);
                dayStart = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), zone));
                dayEnd = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return ContactOperationResult<(ContactAccess, ContactListSpecification, string)>.Failure(new("INTEGRATION_UNAVAILABLE", 503, "Business timezone unavailable")); }
        }
        var owner = a.Authorization.ScopeFilter == RecordAccessScopeFilter.OwnedByMember ? a.Authorization.ScopeOwnerMemberId : null;
        if (a.Authorization.ScopeFilter == RecordAccessScopeFilter.OwnedByMember && string.IsNullOrEmpty(owner)) return ContactOperationResult<(ContactAccess, ContactListSpecification, string)>.Failure(ContactErrors.AccessDenied());
        var searchFields = new[] { "displayName", "workEmail", "personalEmail", "mobilePhone", "workPhone", "otherPhone" }.Where(a.Authorization.CanRead).ToArray();
        var specification = new ContactListSpecification(a.Trusted.WorkspaceId, owner, f, searchFields, FollowUpAuthority: followUp.Authority, DayStart: dayStart, DayEnd: dayEnd);
        var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { a.Trusted.WorkspaceId, a.Trusted.MemberId, owner, Filters = f, searchFields, TasksOwner = followUp.Authority?.ScopeOwnedByMember, followUp.IsAvailable, dayStart, dayEnd }))));
        return ContactOperationResult<(ContactAccess, ContactListSpecification, string)>.Success((a, specification, scope));
    }
    private async Task AuditAsync(ContactAccess access, ContactRequestMetadata metadata, string operation, CancellationToken cancellationToken)
    {
        persistence.AddReadAudit(new ContactReadAuditRecord(operation, access.Trusted.WorkspaceId, access.Trusted.MemberId, null, metadata.RequestId, metadata.CorrelationId, null, timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
    }
    private static ContactOperationResult<ContactListResponse> InvalidCursor() => ContactOperationResult<ContactListResponse>.Failure(ContactErrors.Validation(new Dictionary<string, string[]> { ["cursor"] = ["Invalid or expired cursor for this query scope."] }));
}
