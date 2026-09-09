using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;
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
            throw new ContactsPersistenceConcurrencyException { Source = exception.Source };
        }
        catch (DbUpdateException exception) when (ContainsRelationshipConstraintViolation(exception))
        {
            throw new ContactsRelationshipConflictException { Source = exception.Source };
        }
        catch (DbUpdateException exception) when (ContainsSqlError(exception, 2601) || ContainsSqlError(exception, 2627))
        {
            // A competing request may win a unique idempotency, outbox, or audit key. Do not
            // misreport those infrastructure races as a relationship-domain conflict.
            throw new ContactsPersistenceConcurrencyException { Source = exception.Source };
        }
        catch (Exception exception) when (ContainsSqlError(exception, 1205))
        {
            // SQL Server chooses one SERIALIZABLE contender as a deadlock victim. That loser is
            // a canonical optimistic-concurrency conflict, not an unhandled server failure.
            throw new ContactsPersistenceConcurrencyException { Source = exception.Source };
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

    public async Task<IReadOnlyList<ContactCustomerRelationship>> ReadCustomerRelationshipsAsync(
        string workspaceId, string contactId, CancellationToken cancellationToken) =>
        await dbContext.CustomerRelationships.AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && item.ContactId == contactId)
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
