using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Domain;

namespace UnicoreCRM.Crm.Organizations.Infrastructure.Persistence;

internal sealed class EfOrganizationsPersistence(OrganizationsDbContext dbContext) : IOrganizationsPersistence
{
    public void AddReadAudit(OrganizationReadAuditRecord audit) => dbContext.ReadAuditRecords.Add(audit);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

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
}
