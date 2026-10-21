using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;

internal sealed class ContactLinkReader(ContactsDbContext db, ContactAuthorization authorization) : IContactLinkReader
{
    private static readonly RecordAccessRepresentation Representation = RecordAccessRepresentation.Create(
        "contact.manualReference", ContactFieldSecurity.FieldKeys.Where(x => x != "id").ToArray());
    private static ContactLinkTarget Project(Contact row, RecordAccessAuthorization policy) => new(row.ContactId,
        policy.CanRead("fullName") && policy.CanRead("displayName") ? row.FullName : null,
        row.ArchivedAt is null && row.Status != "archived");

    public async Task<ContactLinkTarget?> ReadAsync(string id, string requestId, string correlationId, CancellationToken ct)
    {
        var result = await authorization.AuthorizeAsync(new(requestId, correlationId), ContactCapabilities.Read, ct, Representation);
        if (!result.IsSuccess || !Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)) return null;
        var access = result.Value!;
        var row = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == access.Trusted.WorkspaceId && x.ContactId == id, ct);
        if (row is null || await authorization.EnforceRecordAsync(access, row, "readContactManualReference", new(requestId, correlationId), ct) is not null) return null;
        return Project(row, access.Authorization);
    }

    public async Task<IReadOnlyList<ContactLinkTarget>> SearchAsync(string search, string requestId, string correlationId, CancellationToken ct)
    {
        var result = await authorization.AuthorizeAsync(new(requestId, correlationId), ContactCapabilities.Read, ct, Representation);
        if (!result.IsSuccess) return [];
        var access = result.Value!;
        if (!access.Authorization.CanRead("fullName") || !access.Authorization.CanRead("displayName")) return [];
        var query = db.Contacts.AsNoTracking().Where(x => x.WorkspaceId == access.Trusted.WorkspaceId && x.ArchivedAt == null && x.Status != "archived");
        if (access.Authorization.ScopeFilter == RecordAccessScopeFilter.OwnedByMember) query = query.Where(x => x.OwnerId == access.Trusted.MemberId);
        else if (access.Authorization.ScopeFilter != RecordAccessScopeFilter.Workspace) return [];
        var rows = await query.Where(x => x.FullName.Contains(search) || x.ContactId == search).OrderBy(x => x.FullName).ThenBy(x => x.ContactId).Take(20).ToListAsync(ct);
        var targets = new List<ContactLinkTarget>();
        foreach (var row in rows)
            if (await authorization.EnforceRecordAsync(access, row, "searchContactManualReferences", new(requestId, correlationId), ct) is null)
                targets.Add(Project(row, access.Authorization));
        return targets;
    }
}
