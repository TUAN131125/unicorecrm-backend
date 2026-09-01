using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Billing.Invoices.Application.Common;
using UnicoreCRM.Billing.Invoices.Domain;

namespace UnicoreCRM.Billing.Invoices.Infrastructure.Persistence;

/// <summary>
/// Invoice reads against the Invoices-owned context only. Every query is Workspace-scoped, and no
/// ordering, paging or filter is applied beyond what current authority admits for the operation.
/// </summary>
internal sealed class EfInvoicesPersistence(InvoicesDbContext dbContext) : IInvoicesPersistence
{
    public async Task<IReadOnlyList<Invoice>> ReadInvoicesAsync(string workspaceId, CancellationToken cancellationToken) =>
        await dbContext.Invoices
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId)
            .ToArrayAsync(cancellationToken);

    public Task<Invoice?> ReadInvoiceAsync(string workspaceId, string invoiceId, CancellationToken cancellationToken) =>
        dbContext.Invoices
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.InvoiceId == invoiceId, cancellationToken);

    public void AddReadAudit(InvoiceReadAuditRecord readAudit) => dbContext.ReadAuditRecords.Add(readAudit);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public Task<bool> RecordExistsAsync(string workspaceId, string recordId, CancellationToken cancellationToken) =>
        dbContext.Invoices
            .AsNoTracking()
            .AnyAsync(item => item.WorkspaceId == workspaceId && item.InvoiceId == recordId, cancellationToken);
}
