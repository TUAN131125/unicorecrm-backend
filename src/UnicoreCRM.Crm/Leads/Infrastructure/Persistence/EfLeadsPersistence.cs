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
        string? ownerId,
        LeadWorkState? workState,
        string? normalizedSearch,
        bool includePhoneSearch,
        DateTimeOffset? cursorUpdatedAt,
        string? cursorLeadId,
        int take,
        CancellationToken cancellationToken)
    {
        var query = FilteredLeads(
            workspaceId, scopeOwnerMemberId, ownerId, workState, normalizedSearch, includePhoneSearch);
        if (cursorUpdatedAt is not null && cursorLeadId is not null)
        {
            query = query.Where(item => item.UpdatedAt < cursorUpdatedAt
                || (item.UpdatedAt == cursorUpdatedAt && string.Compare(item.LeadId, cursorLeadId) < 0));
        }
        return await query
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.LeadId)
            .Take(take)
            .ToArrayAsync(cancellationToken);
    }

    public Task<long> CountLeadsAsync(
        string workspaceId,
        string? scopeOwnerMemberId,
        string? ownerId,
        LeadWorkState? workState,
        string? normalizedSearch,
        bool includePhoneSearch,
        CancellationToken cancellationToken) =>
        FilteredLeads(workspaceId, scopeOwnerMemberId, ownerId, workState, normalizedSearch, includePhoneSearch)
            .LongCountAsync(cancellationToken);

    private IQueryable<Lead> FilteredLeads(
        string workspaceId,
        string? scopeOwnerMemberId,
        string? ownerId,
        LeadWorkState? workState,
        string? normalizedSearch,
        bool includePhoneSearch)
    {
        IQueryable<Lead> query = dbContext.Leads.AsNoTracking().Where(item => item.WorkspaceId == workspaceId);
        // The AccessControl record scope is part of the query, not a post-filter, so hidden rows are
        // never materialised and never reach the ordering or the projection.
        if (scopeOwnerMemberId is not null)
            query = query.Where(item => item.ScopeOwnerId == scopeOwnerMemberId);
        if (ownerId is not null)
            query = query.Where(item => item.ScopeOwnerId == ownerId);
        if (workState is not null)
            query = query.Where(item => item.WorkState == workState);
        if (normalizedSearch is not null)
        {
            query = includePhoneSearch
                ? query.Where(item => item.SearchText.Contains(normalizedSearch)
                    || item.PhoneSearchText.Contains(normalizedSearch))
                : query.Where(item => item.SearchText.Contains(normalizedSearch));
        }
        return query;
    }

    public async Task<long?> ReadCurrentVersionAsync(
        string workspaceId,
        string leadId,
        CancellationToken cancellationToken)
    {
        // A failed optimistic write leaves attempted values tracked. Clear them before asking the
        // database for the version that actually won the race.
        dbContext.ChangeTracker.Clear();
        return await dbContext.Leads
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && item.LeadId == leadId)
            .Select(item => (long?)item.Version)
            .SingleOrDefaultAsync(cancellationToken);
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
