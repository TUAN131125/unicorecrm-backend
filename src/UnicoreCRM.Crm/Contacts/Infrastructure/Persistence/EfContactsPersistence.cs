using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Domain;

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;

internal sealed class EfContactsPersistence(ContactsDbContext dbContext) : IContactsPersistence
{
    public async Task<Application.ListContacts.ContactListSlice> ReadContactPageAsync(Application.ListContacts.ContactListSpecification specification, int limit, CancellationToken cancellationToken)
    {
        var filtered = ContactRows(specification);
        var total = await filtered.LongCountAsync(cancellationToken);
        var page = filtered;
        if (specification.CursorId is { } id)
        {
            if (specification.Filters.Sort == "nameAsc")
            {
                var name = specification.CursorName!;
                page = page.Where(c => string.Compare(c.Contact.FullName, name) > 0 || (c.Contact.FullName == name && string.Compare(c.Contact.ContactId, id) > 0));
            }
            else if (specification.Filters.Sort == "nextFollowUp")
            {
                var due = specification.CursorFollowUpAt;
                page = specification.CursorFollowUpIsNull
                    ? page.Where(c => c.NextFollowUpAt == null && string.Compare(c.Contact.ContactId, id) > 0)
                    : page.Where(c => c.NextFollowUpAt == null || c.NextFollowUpAt > due || (c.NextFollowUpAt == due && string.Compare(c.Contact.ContactId, id) > 0));
            }
            else
            {
                var updated = specification.CursorUpdatedAt!.Value;
                page = page.Where(c => c.Contact.UpdatedAt < updated || (c.Contact.UpdatedAt == updated && string.Compare(c.Contact.ContactId, id) < 0));
            }
        }
        page = specification.Filters.Sort switch
        {
            "nameAsc" => page.OrderBy(c => c.Contact.FullName).ThenBy(c => c.Contact.ContactId),
            "nextFollowUp" => page.OrderBy(c => c.NextFollowUpAt == null).ThenBy(c => c.NextFollowUpAt).ThenBy(c => c.Contact.ContactId),
            _ => page.OrderByDescending(c => c.Contact.UpdatedAt).ThenByDescending(c => c.Contact.ContactId)
        };
        var rows = await page.Take(limit).ToArrayAsync(cancellationToken);
        return new(rows.Select(row => new Application.ListContacts.ContactPageRow(row.Contact, row.NextFollowUpAt)).ToArray(), total);
    }

    public async Task<IReadOnlyDictionary<string, long>> ReadContactStatusCountsAsync(Application.ListContacts.ContactListSpecification specification, CancellationToken cancellationToken)
        => await ContactRows(specification).GroupBy(c => c.Contact.Status)
            .Select(g => new { Status = g.Key, Count = g.LongCount() }).ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);

    private sealed class ContactSqlRow
    {
        public Contact Contact { get; init; } = null!;
        public DateTimeOffset? NextFollowUpAt { get; init; }
    }

    private IQueryable<ContactSqlRow> ContactRows(Application.ListContacts.ContactListSpecification specification)
    {
        var contacts = ContactListSql.Filter(dbContext, specification);
        IQueryable<ContactSqlRow> rows;
        if (specification.FollowUpAuthority is { } authority)
        {
            var followUps = authority.Compose(dbContext.Set<UnicoreCRM.Operations.Tasks.Contracts.ContactFollowUpProjectionRow>().AsNoTracking());
            rows = from contact in contacts
                   join followUp in followUps on new { contact.WorkspaceId, ContactId = contact.ContactId } equals new { followUp.WorkspaceId, followUp.ContactId } into joined
                   from followUp in joined.DefaultIfEmpty()
                   select new ContactSqlRow { Contact = contact, NextFollowUpAt = (DateTimeOffset?)followUp.NextFollowUpAt };
        }
        else rows = contacts.Select(c => new ContactSqlRow { Contact = c, NextFollowUpAt = null });
        if (specification.DayStart is { } start)
            rows = specification.Filters.FollowUp == "overdue"
                ? rows.Where(row => row.NextFollowUpAt < start)
                : rows.Where(row => row.NextFollowUpAt >= start && row.NextFollowUpAt < specification.DayEnd);
        return rows;
    }

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
    public void AddIdempotency(ContactIdempotencyRecord record) => dbContext.IdempotencyRecords.Add(record);
    public Task<ContactIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken) =>
        dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ContactsPersistenceConcurrencyException(exception) { Source = exception.Source };
        }
        catch (DbUpdateException exception) when (ContainsRelationshipConstraintViolation(exception))
        {
            throw new ContactsRelationshipConflictException { Source = exception.Source };
        }
        catch (DbUpdateException exception) when (ContainsSqlError(exception, 2601) || ContainsSqlError(exception, 2627))
        {
            // A competing request may win a unique idempotency, outbox, or audit key. Do not
            // misreport those infrastructure races as a relationship-domain conflict.
            throw new ContactsPersistenceConcurrencyException(exception) { Source = exception.Source };
        }
        catch (Exception exception) when (ContainsSqlError(exception, 1205))
        {
            // SQL Server chooses one SERIALIZABLE contender as a deadlock victim. That loser is
            // a canonical optimistic-concurrency conflict, not an unhandled server failure.
            throw new ContactsPersistenceConcurrencyException(exception) { Source = exception.Source };
        }
    }

    private static bool ContainsSqlError(Exception exception, int number)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sql && sql.Number == number) return true;
        return false;
    }

    private static bool ContainsRelationshipConstraintViolation(Exception exception)
    {
        string[] relationshipIndexes =
        [
            "IX_OrganizationRelationships_WorkspaceId_ContactId_OrganizationId",
            "IX_OrganizationRelationships_WorkspaceId_ContactId",
            "IX_CustomerRelationships_WorkspaceId_ContactId_CustomerId",
            "IX_CustomerRelationships_WorkspaceId_CustomerId"
        ];

        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sql && sql.Number is 2601 or 2627
                && relationshipIndexes.Any(index => sql.Message.Contains(index, StringComparison.Ordinal)))
                return true;
        return false;
    }

    public Task<Contact?> ReadContactAsync(
        string workspaceId,
        string contactId,
        CancellationToken cancellationToken) =>
        dbContext.Contacts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId && item.ContactId == contactId,
                cancellationToken);

    public Task<Contact?> LoadContactAsync(string workspaceId, string contactId, CancellationToken cancellationToken) =>
        dbContext.Contacts.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.ContactId == contactId, cancellationToken);

    public async Task<IReadOnlyList<ContactOrganizationRelationship>> ReadOrganizationRelationshipsAsync(
        string workspaceId, string contactId, CancellationToken cancellationToken) =>
        await dbContext.OrganizationRelationships.AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && item.ContactId == contactId)
            .OrderBy(item => item.EffectiveTo == null ? 0 : 1)
            .ThenByDescending(item => item.EffectiveFrom).ThenBy(item => item.RelationshipId)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Contact>> ReadActiveOrganizationContactsAsync(string workspaceId, string organizationId, CancellationToken cancellationToken)
    {
        var ids = dbContext.OrganizationRelationships.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.OrganizationId == organizationId && x.EffectiveTo == null)
            .Select(x => x.ContactId);
        return await dbContext.Contacts.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId && x.ArchivedAt == null && ids.Contains(x.ContactId))
            .OrderBy(x => x.ContactId).ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ContactCustomerRelationship>> ReadCustomerRelationshipsAsync(
        string workspaceId, string contactId, CancellationToken cancellationToken) =>
        await dbContext.CustomerRelationships.AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && item.ContactId == contactId)
            .OrderBy(item => item.EffectiveTo == null ? 0 : 1)
            .ThenByDescending(item => item.EffectiveFrom).ThenBy(item => item.RelationshipId)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<ContactCustomerRelationship>> ReadCustomerStakeholderRelationshipsAsync(
        string workspaceId, string customerId, CancellationToken cancellationToken) =>
        await dbContext.CustomerRelationships.AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && item.CustomerId == customerId)
            .OrderBy(item => item.EffectiveTo == null ? 0 : 1)
            .ThenByDescending(item => item.EffectiveFrom).ThenBy(item => item.RelationshipId)
            .ToArrayAsync(cancellationToken);

    public Task<ContactOrganizationRelationship?> LoadOrganizationRelationshipAsync(
        string workspaceId, string contactId, string relationshipId, CancellationToken cancellationToken) =>
        dbContext.OrganizationRelationships.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.ContactId == contactId && item.RelationshipId == relationshipId, cancellationToken);

    public Task<ContactCustomerRelationship?> LoadCustomerRelationshipAsync(
        string workspaceId, string contactId, string relationshipId, CancellationToken cancellationToken) =>
        dbContext.CustomerRelationships.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.ContactId == contactId && item.RelationshipId == relationshipId, cancellationToken);

    public Task<ContactOrganizationRelationship?> LoadActivePrimaryOrganizationRelationshipAsync(
        string workspaceId, string contactId, string? exceptRelationshipId, CancellationToken cancellationToken) =>
        dbContext.OrganizationRelationships.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.ContactId == contactId
            && item.EffectiveTo == null && item.IsPrimaryAffiliation && item.RelationshipId != exceptRelationshipId, cancellationToken);

    public Task<bool> HasActiveOrganizationRelationshipAsync(string workspaceId, string contactId, string organizationId, CancellationToken cancellationToken) =>
        dbContext.OrganizationRelationships.AnyAsync(item => item.WorkspaceId == workspaceId && item.ContactId == contactId
            && item.OrganizationId == organizationId && item.EffectiveTo == null, cancellationToken);

    public Task<bool> HasActiveCustomerRelationshipAsync(string workspaceId, string contactId, string customerId, CancellationToken cancellationToken) =>
        dbContext.CustomerRelationships.AnyAsync(item => item.WorkspaceId == workspaceId && item.ContactId == contactId
            && item.CustomerId == customerId && item.EffectiveTo == null, cancellationToken);

    public Task<bool> HasOtherActivePrimaryCustomerRelationshipAsync(string workspaceId, string customerId, string? exceptRelationshipId, CancellationToken cancellationToken) =>
        dbContext.CustomerRelationships.AnyAsync(item => item.WorkspaceId == workspaceId && item.CustomerId == customerId
            && item.Role == "primary_contact" && item.EffectiveTo == null && item.RelationshipId != exceptRelationshipId, cancellationToken);

    public void AddOrganizationRelationship(ContactOrganizationRelationship relationship) => dbContext.OrganizationRelationships.Add(relationship);
    public void AddCustomerRelationship(ContactCustomerRelationship relationship) => dbContext.CustomerRelationships.Add(relationship);

    public async Task<IReadOnlyList<Contact>> ReadContactsAsync(
        string workspaceId,
        string? scopeOwnerMemberId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Contacts
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && item.ArchivedAt == null);
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
