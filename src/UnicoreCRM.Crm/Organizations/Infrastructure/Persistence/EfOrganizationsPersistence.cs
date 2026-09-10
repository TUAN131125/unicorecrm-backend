using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Domain;

namespace UnicoreCRM.Crm.Organizations.Infrastructure.Persistence;

internal sealed class EfOrganizationsPersistence(OrganizationsDbContext dbContext) : IOrganizationsPersistence
{
    public async Task<IOrganizationsTransaction> BeginSerializableAsync(CancellationToken cancellationToken) =>
        new Transaction(await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken), dbContext);
    public void AddOrganization(Organization organization) => dbContext.Organizations.Add(organization);
    public void AddIdempotency(OrganizationIdempotencyRecord record) => dbContext.IdempotencyRecords.Add(record);
    public void AddAudit(OrganizationAuditRecord record) => dbContext.AuditRecords.Add(record);
    public void AddOutbox(OrganizationOutboxMessage message) => dbContext.OutboxMessages.Add(message);
    public Task<OrganizationIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken) => dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scopeKey, cancellationToken);
    public void AddReadAudit(OrganizationReadAuditRecord audit) => dbContext.ReadAuditRecords.Add(audit);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new OrganizationsPersistenceConcurrencyException(); }
        catch (DbUpdateException) { throw new OrganizationsPersistenceConcurrencyException(); }
    }

    public Task<Organization?> LoadOrganizationAsync(string workspaceId, string organizationId, CancellationToken cancellationToken) =>
        dbContext.Organizations.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.OrganizationId == organizationId, cancellationToken);

    public Task<Organization?> ReadOrganizationAsync(
        string workspaceId,
        string organizationId,
        CancellationToken cancellationToken) =>
        dbContext.Organizations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId && item.OrganizationId == organizationId,
                cancellationToken);

    public async Task<IReadOnlyList<Organization>> ReadOrganizationsAsync(
        string workspaceId,
        CancellationToken cancellationToken) =>
        await dbContext.Organizations
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.OrganizationId)
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<Organization>> ListOrganizationsAsync(
        string workspaceId, string? scopeOwnerMemberId, string? ownerId, string? status,
        string? industry, string? sizeBand, string? normalizedSearch,
        DateTimeOffset? cursorCreatedAt, string? cursorOrganizationId, int take,
        CancellationToken cancellationToken)
    {
        IQueryable<Organization> query = dbContext.Organizations.AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId);
        if (scopeOwnerMemberId is not null)
            query = query.Where(item => item.OwnerId == scopeOwnerMemberId);
        if (ownerId is not null)
            query = query.Where(item => item.OwnerId == ownerId);
        query = status is null
            ? query.Where(item => item.Status != "archived")
            : query.Where(item => item.Status == status);
        if (industry is not null)
            query = query.Where(item => item.Industry == industry);
        if (sizeBand is not null)
            query = query.Where(item => item.SizeBand == sizeBand);
        if (normalizedSearch is not null)
            query = query.Where(item => item.SearchText.Contains(normalizedSearch));
        if (cursorCreatedAt is not null && cursorOrganizationId is not null)
            query = query.Where(item => item.CreatedAt < cursorCreatedAt
                || (item.CreatedAt == cursorCreatedAt && string.Compare(item.OrganizationId, cursorOrganizationId) > 0));
        return await query.OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.OrganizationId)
            .Take(take)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Organization>> ReadOrganizationsAsync(
        string workspaceId, IReadOnlyCollection<string> organizationIds, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && organizationIds.Contains(item.OrganizationId))
            .ToArrayAsync(cancellationToken);

    private sealed class Transaction(IDbContextTransaction transaction, OrganizationsDbContext context) : IOrganizationsTransaction
    {
        private bool committed;
        public async Task CommitAsync(CancellationToken cancellationToken) { await transaction.CommitAsync(cancellationToken); committed = true; }
        public async ValueTask DisposeAsync() { await transaction.DisposeAsync(); if (!committed) context.ChangeTracker.Clear(); }
    }
}
