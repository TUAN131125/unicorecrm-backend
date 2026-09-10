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
