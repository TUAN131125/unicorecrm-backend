using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Domain;

namespace UnicoreCRM.Crm.Customers.Infrastructure.Persistence;

internal sealed class EfCustomersPersistence(CustomersDbContext dbContext) : ICustomersPersistence
{
    public async Task<ICustomersTransaction> BeginSerializableAsync(CancellationToken cancellationToken) =>
        new Transaction(await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken), dbContext);
    public void AddCustomer(Customer customer) => dbContext.Customers.Add(customer);
    public void AddIdempotency(CustomerIdempotencyRecord record) => dbContext.IdempotencyRecords.Add(record);
    public void AddLeadConversionProvenance(CustomerLeadConversionProvenance provenance) => dbContext.LeadConversionProvenance.Add(provenance);
    public Task<CustomerLeadConversionProvenance?> LoadLeadConversionProvenanceAsync(string workspaceId,string workflowId,CancellationToken cancellationToken) =>
        dbContext.LeadConversionProvenance.SingleOrDefaultAsync(x=>x.WorkspaceId==workspaceId&&x.WorkflowId==workflowId,cancellationToken);
    public void AddAudit(CustomerAuditRecord record) => dbContext.AuditRecords.Add(record);
    public void AddOutbox(CustomerOutboxMessage message) => dbContext.OutboxMessages.Add(message);
    public void AddReadAudit(CustomerReadAuditRecord audit) => dbContext.ReadAuditRecords.Add(audit);
    public Task<CustomerIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken) =>
        dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scopeKey, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new CustomersPersistenceConcurrencyException(); }
        catch (DbUpdateException exception) when (ContainsCustomerBusinessKey(exception)) { throw new CustomerBusinessKeyConflictException(); }
        catch (DbUpdateException exception) when (ContainsSqlError(exception, 2601) || ContainsSqlError(exception, 2627)) { throw new CustomersPersistenceConcurrencyException(); }
        catch (Exception exception) when (ContainsSqlError(exception, 1205)) { throw new CustomersPersistenceConcurrencyException(); }
    }

    public Task<Customer?> LoadCustomerAsync(string workspaceId, string customerId, CancellationToken cancellationToken) =>
        dbContext.Customers.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.CustomerId == customerId, cancellationToken);
    public Task<Customer?> ReadCustomerAsync(string workspaceId, string customerId, CancellationToken cancellationToken) =>
        dbContext.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.CustomerId == customerId, cancellationToken);
    public Task<bool> SubjectExistsAsync(string workspaceId, string relationshipType, string relationshipId, CancellationToken cancellationToken) =>
        dbContext.Customers.AnyAsync(x => x.WorkspaceId == workspaceId && x.RelationshipType == relationshipType && x.RelationshipId == relationshipId, cancellationToken);
    public Task<Customer?> LoadBySubjectAsync(string workspaceId, string relationshipType, string relationshipId, CancellationToken cancellationToken) =>
        dbContext.Customers.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.RelationshipType == relationshipType && x.RelationshipId == relationshipId, cancellationToken);

    public async Task<IReadOnlyList<Customer>> ReadCustomersAsync(string workspaceId, CancellationToken cancellationToken) =>
        await dbContext.Customers.AsNoTracking().Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.CustomerId).ToArrayAsync(cancellationToken);
    public async Task<IReadOnlyList<Customer>> ReadCustomersAsync(string workspaceId, IReadOnlyCollection<string> customerIds, CancellationToken cancellationToken) =>
        await dbContext.Customers.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && customerIds.Contains(x.CustomerId)).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Customer>> ListCustomersAsync(string workspaceId, string? scopeOwnerMemberId, string? ownerId,
        string? type, string? status, string? segment, string? tier, string? normalizedSearch,
        DateTimeOffset? cursorCreatedAt, string? cursorCustomerId, int take, CancellationToken cancellationToken)
    {
        IQueryable<Customer> query = dbContext.Customers.AsNoTracking().Where(x => x.WorkspaceId == workspaceId);
        if (scopeOwnerMemberId is not null) query = query.Where(x => x.OwnerId == scopeOwnerMemberId);
        if (ownerId is not null) query = query.Where(x => x.OwnerId == ownerId);
        query = status is null ? query.Where(x => x.Status != "ARCHIVED") : query.Where(x => x.Status == status);
        if (type is not null) query = query.Where(x => x.Type == type);
        if (segment is not null) query = query.Where(x => x.Segment == segment);
        if (tier is not null) query = query.Where(x => x.Tier == tier);
        if (normalizedSearch is not null) query = query.Where(x => x.SearchText.Contains(normalizedSearch));
        if (cursorCreatedAt is not null && cursorCustomerId is not null)
            query = query.Where(x => x.CreatedAt < cursorCreatedAt || (x.CreatedAt == cursorCreatedAt && string.Compare(x.CustomerId, cursorCustomerId) > 0));
        return await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.CustomerId).Take(take).ToArrayAsync(cancellationToken);
    }

    private static bool ContainsSqlError(Exception exception, int number)
    { for (Exception? current = exception; current is not null; current = current.InnerException) if (current is SqlException sql && sql.Number == number) return true; return false; }
    private static bool ContainsCustomerBusinessKey(Exception exception)
    {
        string[] indexes = ["IX_Customers_WorkspaceId_RelationshipType_RelationshipId", "IX_Customers_WorkspaceId_CustomerCode"];
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException sql && sql.Number is 2601 or 2627 && indexes.Any(i => sql.Message.Contains(i, StringComparison.Ordinal))) return true;
        return false;
    }

    private sealed class Transaction(IDbContextTransaction transaction, CustomersDbContext context) : ICustomersTransaction
    {
        private bool committed;
        public async Task CommitAsync(CancellationToken cancellationToken) { await transaction.CommitAsync(cancellationToken); committed = true; }
        public async ValueTask DisposeAsync() { await transaction.DisposeAsync(); if (!committed) context.ChangeTracker.Clear(); }
    }
}
