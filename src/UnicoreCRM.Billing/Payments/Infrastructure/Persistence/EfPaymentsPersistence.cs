using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Billing.Payments.Application.Common;
using UnicoreCRM.Billing.Payments.Domain;

namespace UnicoreCRM.Billing.Payments.Infrastructure.Persistence;

internal sealed class EfPaymentsPersistence(PaymentsDbContext dbContext) : IPaymentsPersistence
{
    public async Task<IReadOnlyList<PaymentPlan>> ReadPaymentPlansAsync(string workspaceId, string? orderId, CancellationToken cancellationToken)
    {
        IQueryable<PaymentPlan> query = dbContext.PaymentPlans.AsNoTracking().Where(item => item.WorkspaceId == workspaceId);
        if (orderId is not null) query = query.Where(item => item.OrderId == orderId);
        return await query.ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentScheduleLine>> ReadPaymentScheduleLinesAsync(string workspaceId, string? planId, CancellationToken cancellationToken)
    {
        IQueryable<PaymentScheduleLine> query = dbContext.PaymentScheduleLines.AsNoTracking().Where(item => item.WorkspaceId == workspaceId);
        if (planId is not null) query = query.Where(item => item.PaymentPlanId == planId);
        return await query.ToArrayAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentIntent>> ReadPaymentIntentsAsync(string workspaceId, string? orderId, CancellationToken cancellationToken)
    {
        IQueryable<PaymentIntent> query = dbContext.PaymentIntents.AsNoTracking().Where(item => item.WorkspaceId == workspaceId);
        if (orderId is not null) query = query.Where(item => item.OrderId == orderId);
        return await query.ToArrayAsync(cancellationToken);
    }

    public Task<PaymentIntent?> ReadPaymentIntentAsync(string workspaceId, string paymentIntentId, CancellationToken cancellationToken) =>
        dbContext.PaymentIntents.AsNoTracking().SingleOrDefaultAsync(
            item => item.WorkspaceId == workspaceId && item.PaymentIntentId == paymentIntentId,
            cancellationToken);

    public async Task<IReadOnlyList<PaymentRecord>> ReadPaymentRecordsAsync(string workspaceId, string? buyerId, CancellationToken cancellationToken)
    {
        IQueryable<PaymentRecord> query = dbContext.PaymentRecords.AsNoTracking().Where(item => item.WorkspaceId == workspaceId);
        if (buyerId is not null) query = query.Where(item => item.BuyerId == buyerId);
        return await query.ToArrayAsync(cancellationToken);
    }

    public Task<PaymentRecord?> ReadPaymentRecordAsync(string workspaceId, string paymentRecordId, CancellationToken cancellationToken) =>
        dbContext.PaymentRecords.AsNoTracking().SingleOrDefaultAsync(
            item => item.WorkspaceId == workspaceId && item.PaymentRecordId == paymentRecordId,
            cancellationToken);

    public void AddReadAudit(PaymentReadAuditRecord readAudit) => dbContext.ReadAuditRecords.Add(readAudit);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public async Task<bool> RecordExistsAsync(string workspaceId, string recordId, CancellationToken cancellationToken) =>
        await dbContext.PaymentPlans.AsNoTracking().AnyAsync(item => item.WorkspaceId == workspaceId && item.PaymentPlanId == recordId, cancellationToken)
        || await dbContext.PaymentScheduleLines.AsNoTracking().AnyAsync(item => item.WorkspaceId == workspaceId && item.PaymentScheduleLineId == recordId, cancellationToken)
        || await dbContext.PaymentIntents.AsNoTracking().AnyAsync(item => item.WorkspaceId == workspaceId && item.PaymentIntentId == recordId, cancellationToken)
        || await dbContext.PaymentRecords.AsNoTracking().AnyAsync(item => item.WorkspaceId == workspaceId && item.PaymentRecordId == recordId, cancellationToken);
}
