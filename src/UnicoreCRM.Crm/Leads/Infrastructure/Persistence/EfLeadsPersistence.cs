using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Infrastructure.Persistence;

internal sealed class EfLeadsPersistence(LeadsDbContext dbContext) : ILeadsPersistence
{
    public async Task<ILeadsTransaction> BeginSerializableAsync(CancellationToken cancellationToken) =>
        new LeadsTransaction(await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    public Task<Lead?> LoadLeadAsync(string workspaceId, string leadId, CancellationToken cancellationToken) =>
        dbContext.Leads.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.LeadId == leadId, cancellationToken);

    public Task<Lead?> ReadLeadAsync(string workspaceId, string leadId, CancellationToken cancellationToken) =>
        dbContext.Leads.AsNoTracking().SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.LeadId == leadId, cancellationToken);

    public async Task<IReadOnlyList<Lead>> ListLeadsAsync(
        string workspaceId,
        string? scopeOwnerMemberId,
        CancellationToken cancellationToken)
    {
        IQueryable<Lead> query = dbContext.Leads.AsNoTracking().Where(item => item.WorkspaceId == workspaceId);
        // The AccessControl record scope is part of the query, not a post-filter, so hidden rows are
        // never materialised and never reach the ordering or the projection.
        if (scopeOwnerMemberId is not null)
            query = query.Where(item => item.ScopeOwnerId == scopeOwnerMemberId);
        return await query
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.LeadId)
            .ToArrayAsync(cancellationToken);
    }

    public Task<LeadIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken) =>
        dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);

    public void AddLead(Lead lead) => dbContext.Leads.Add(lead);
    public void AddIdempotency(LeadIdempotencyRecord record) => dbContext.IdempotencyRecords.Add(record);
    public void AddAudit(LeadAuditRecord audit) => dbContext.AuditRecords.Add(audit);
    public void AddOutbox(LeadOutboxMessage message) => dbContext.OutboxMessages.Add(message);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new LeadsPersistenceConcurrencyException(exception);
        }
    }

    private sealed class LeadsTransaction(IDbContextTransaction transaction) : ILeadsTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
