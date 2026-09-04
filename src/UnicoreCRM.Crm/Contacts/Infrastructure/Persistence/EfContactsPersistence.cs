using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Domain;

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;

internal sealed class EfContactsPersistence(ContactsDbContext dbContext) : IContactsPersistence
{
    public void AddReadAudit(ContactReadAuditRecord audit) => dbContext.ReadAuditRecords.Add(audit);

    public async Task<IContactsTransaction> BeginSerializableAsync(CancellationToken cancellationToken) =>
        new ContactsTransaction(
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken), dbContext);

    // Two single-column seeks rather than one OR predicate: each seek is guaranteed to take a
    // SERIALIZABLE key-range lock on its own index, which is what actually blocks a concurrent
    // insert of the same address. An OR could be satisfied by a scan and would make the locking
    // behaviour depend on the optimizer.
    public async Task<bool> AnyContactWithNormalizedEmailAsync(
        string workspaceId,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        var work = await dbContext.Contacts.AnyAsync(
            item => item.WorkspaceId == workspaceId && item.NormalizedWorkEmail == normalizedEmail,
            cancellationToken);
        var personal = await dbContext.Contacts.AnyAsync(
            item => item.WorkspaceId == workspaceId && item.NormalizedPersonalEmail == normalizedEmail,
            cancellationToken);
        // Both seeks always run so both range locks are always taken; short-circuiting on the first
        // hit would leave the second index unlocked and reopen the race it exists to close.
        return work || personal;
    }

    public Task<ContactConversionRecord?> FindConversionAsync(string scopeKey, CancellationToken cancellationToken) =>
        dbContext.ConversionRecords.SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);

    public void AddContact(Contact contact) => dbContext.Contacts.Add(contact);
    public void AddConversion(ContactConversionRecord record) => dbContext.ConversionRecords.Add(record);
    public void AddAudit(ContactAuditRecord audit) => dbContext.AuditRecords.Add(audit);
    public void AddOutbox(ContactOutboxMessage message) => dbContext.OutboxMessages.Add(message);

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

    private sealed class ContactsTransaction(IDbContextTransaction transaction, ContactsDbContext context) : IContactsTransaction
    {
        private bool committed;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            if (!committed)
                context.ChangeTracker.Clear();
        }
    }
}
