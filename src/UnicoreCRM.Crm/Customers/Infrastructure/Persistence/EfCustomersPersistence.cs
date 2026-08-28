using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Domain;

namespace UnicoreCRM.Crm.Customers.Infrastructure.Persistence;

internal sealed class EfCustomersPersistence(CustomersDbContext dbContext) : ICustomersPersistence
{
    public void AddReadAudit(CustomerReadAuditRecord audit) => dbContext.ReadAuditRecords.Add(audit);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public Task<Customer?> ReadCustomerAsync(
        string workspaceId,
        string customerId,
        CancellationToken cancellationToken) =>
        dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId && item.CustomerId == customerId,
                cancellationToken);

    public async Task<IReadOnlyList<Customer>> ReadCustomersAsync(
        string workspaceId,
        CancellationToken cancellationToken) =>
        await dbContext.Customers
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            // The wire admits no ordering parameter or promise. This order is deterministic only.
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.CustomerId)
            .ToArrayAsync(cancellationToken);
}
