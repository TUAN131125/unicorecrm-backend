using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Domain;

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;

internal sealed class EfContactsPersistence(ContactsDbContext dbContext) : IContactsPersistence
{
    public void AddReadAudit(ContactReadAuditRecord audit) => dbContext.ReadAuditRecords.Add(audit);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public Task<Contact?> ReadContactAsync(
        string workspaceId,
        string contactId,
        CancellationToken cancellationToken) =>
        dbContext.Contacts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId && item.ContactId == contactId,
                cancellationToken);

    public async Task<IReadOnlyList<Contact>> ReadContactsAsync(
        string workspaceId,
        string? scopeOwnerMemberId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Contacts
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId);
        if (scopeOwnerMemberId is not null)
            query = query.Where(item => item.OwnerId == scopeOwnerMemberId);

        return await query
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.ContactId)
            .ToArrayAsync(cancellationToken);
    }
}
