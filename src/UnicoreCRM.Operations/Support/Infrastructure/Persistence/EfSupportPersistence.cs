using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Operations.Support.Domain;

namespace UnicoreCRM.Operations.Support.Infrastructure.Persistence;

internal sealed class EfSupportPersistence(SupportDbContext dbContext) : ISupportPersistence
{
    public async Task<ISupportTransaction> BeginSerializableAsync(CancellationToken cancellationToken) =>
        new SupportTransaction(await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    public Task<SupportCase?> LoadCaseAsync(string workspaceId, string caseId, CancellationToken cancellationToken) =>
        dbContext.Cases.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.CaseId == caseId, cancellationToken);

    public Task<SupportCase?> ReadCaseAsync(string workspaceId, string caseId, CancellationToken cancellationToken) =>
        dbContext.Cases.AsNoTracking().SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.CaseId == caseId, cancellationToken);

    /// <summary>
    /// Allocates the next Workspace-and-year case sequence. Callers run this inside the
    /// SERIALIZABLE create transaction, and a unique index on (WorkspaceId, CaseNumber)
    /// backstops the allocation.
    /// </summary>
    public async Task<int> MaxCaseSequenceAsync(string workspaceId, int caseYear, CancellationToken cancellationToken) =>
        await dbContext.Cases
            .Where(item => item.WorkspaceId == workspaceId && item.CaseYear == caseYear)
            .Select(item => (int?)item.CaseSequence)
            .MaxAsync(cancellationToken) ?? 0;

    public async Task<SupportPage<SupportCase>> ListCasesAsync(
        string workspaceId,
        SupportCaseListSpecification specification,
        CancellationToken cancellationToken)
    {
        // SLA state is a recorded SUPPORT SLA AUTHORITY_GAP, so the projection reports only
        // not_applicable. A filter for any other declared SLA value asks a question Support
        // cannot answer; it matches nothing rather than returning cases whose SLA state Support
        // has not determined. The value is still validated against the declared vocabulary, so
        // an undeclared value is rejected rather than silently emptied.
        if (specification.SlaStatus is not null
            && specification.SlaStatus != SupportProjection.UnresolvedSlaStatus)
        {
            return new SupportPage<SupportCase>([], false, null, 0);
        }

        IQueryable<SupportCase> query = dbContext.Cases.AsNoTracking().Where(item => item.WorkspaceId == workspaceId);
        if (!string.IsNullOrEmpty(specification.Search))
        {
            query = query.Where(item =>
                item.Title.Contains(specification.Search)
                || item.Description.Contains(specification.Search)
                || item.CaseNumber.Contains(specification.Search));
        }
        if (specification.Status is not null)
            query = query.Where(item => item.Status == specification.Status);
        if (specification.Priority is not null)
            query = query.Where(item => item.Priority == specification.Priority);
        if (specification.Category is not null)
            query = query.Where(item => item.Category == specification.Category);
        if (specification.OwnerId is not null)
            query = query.Where(item => item.OwnerId == specification.OwnerId);
        if (specification.RelationshipType is not null)
            query = query.Where(item => item.RelationshipType == specification.RelationshipType);
        if (specification.RelationshipId is not null)
            query = query.Where(item => item.RelationshipId == specification.RelationshipId);

        var totalCount = await query.CountAsync(cancellationToken);
        query = Order(query, specification.SortBy, specification.Descending);
        var items = await query.Skip(specification.Offset).Take(specification.Limit + 1).ToListAsync(cancellationToken);
        var hasNext = items.Count > specification.Limit;
        if (hasNext)
            items.RemoveAt(items.Count - 1);
        return new SupportPage<SupportCase>(items, hasNext, hasNext ? specification.Offset + specification.Limit : null, totalCount);
    }

    public Task<SupportIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken) =>
        dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken);

    public void AddCase(SupportCase supportCase) => dbContext.Cases.Add(supportCase);
    public void AddComment(SupportCaseComment comment) => dbContext.Comments.Add(comment);
    public void AddIdempotency(SupportIdempotencyRecord record) => dbContext.IdempotencyRecords.Add(record);
    public void AddAudit(SupportAuditRecord audit) => dbContext.AuditRecords.Add(audit);
    public void AddOutbox(SupportOutboxMessage message) => dbContext.OutboxMessages.Add(message);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new SupportPersistenceConcurrencyException(exception);
        }
    }

    private static IQueryable<SupportCase> Order(IQueryable<SupportCase> query, string sortBy, bool descending) =>
        (sortBy, descending) switch
        {
            ("createdAt", true) => query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.CaseId),
            ("createdAt", false) => query.OrderBy(item => item.CreatedAt).ThenBy(item => item.CaseId),
            ("priority", true) => query.OrderByDescending(item => item.Priority).ThenByDescending(item => item.CaseId),
            ("priority", false) => query.OrderBy(item => item.Priority).ThenBy(item => item.CaseId),
            ("resolutionDueAt", true) => query.OrderByDescending(item => item.ResolutionDueAt).ThenByDescending(item => item.CaseId),
            ("resolutionDueAt", false) => query.OrderBy(item => item.ResolutionDueAt).ThenBy(item => item.CaseId),
            ("caseNumber", true) => query.OrderByDescending(item => item.CaseYear).ThenByDescending(item => item.CaseSequence).ThenByDescending(item => item.CaseId),
            ("caseNumber", false) => query.OrderBy(item => item.CaseYear).ThenBy(item => item.CaseSequence).ThenBy(item => item.CaseId),
            ("updatedAt", false) => query.OrderBy(item => item.UpdatedAt).ThenBy(item => item.CaseId),
            _ => query.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.CaseId)
        };

    private sealed class SupportTransaction(IDbContextTransaction transaction) : ISupportTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
