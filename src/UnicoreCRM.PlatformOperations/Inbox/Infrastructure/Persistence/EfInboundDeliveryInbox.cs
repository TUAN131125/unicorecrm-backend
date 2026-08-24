using Microsoft.EntityFrameworkCore;
using UnicoreCRM.PlatformOperations.Inbox.Contracts;
using UnicoreCRM.PlatformOperations.Inbox.Domain;

namespace UnicoreCRM.PlatformOperations.Inbox.Infrastructure.Persistence;

internal sealed class EfInboundDeliveryInbox(InboxDbContext dbContext) : IInboundDeliveryInbox
{
    public async Task<InboxAdmission> AdmitAsync(
        InboundDelivery delivery,
        CancellationToken cancellationToken)
    {
        var existing = await LoadAsync(delivery.IntegrationId, delivery.DeliveryId, cancellationToken);
        if (existing is not null)
            return await EvaluateExistingAsync(existing, delivery, cancellationToken);

        var message = new InboxMessage(
            delivery.IntegrationId,
            delivery.DeliveryId,
            delivery.PayloadHash,
            delivery.ProviderCode,
            delivery.WorkspaceId,
            delivery.DelegatedMemberId,
            delivery.CorrelationId,
            delivery.ReceivedAt);
        dbContext.Messages.Add(message);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new InboxAdmission(InboxAdmissionKind.Accepted, message.InboxMessageId, null, null);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(message).State = EntityState.Detached;
            existing = await LoadAsync(delivery.IntegrationId, delivery.DeliveryId, cancellationToken);
            if (existing is null)
                throw;
            return await EvaluateExistingAsync(existing, delivery, cancellationToken);
        }
    }

    public async Task CompleteAsync(
        string inboxMessageId,
        string leadId,
        string resultCode,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var message = await dbContext.Messages.SingleAsync(
            item => item.InboxMessageId == inboxMessageId,
            cancellationToken);
        message.Complete(leadId, resultCode, completedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(
        string inboxMessageId,
        string resultCode,
        DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        var message = await dbContext.Messages.SingleAsync(
            item => item.InboxMessageId == inboxMessageId,
            cancellationToken);
        message.Fail(resultCode, failedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<InboxMessage?> LoadAsync(
        string integrationId,
        string deliveryId,
        CancellationToken cancellationToken) =>
        dbContext.Messages.SingleOrDefaultAsync(
            item => item.IntegrationId == integrationId && item.DeliveryId == deliveryId,
            cancellationToken);

    private async Task<InboxAdmission> EvaluateExistingAsync(
        InboxMessage existing,
        InboundDelivery delivery,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(existing.PayloadHash, delivery.PayloadHash, StringComparison.Ordinal)
            || !string.Equals(existing.ProviderCode, delivery.ProviderCode, StringComparison.Ordinal)
            || !string.Equals(existing.WorkspaceId, delivery.WorkspaceId, StringComparison.Ordinal)
            || !string.Equals(existing.DelegatedMemberId, delivery.DelegatedMemberId, StringComparison.Ordinal))
        {
            return new InboxAdmission(
                InboxAdmissionKind.Conflict,
                existing.InboxMessageId,
                existing.ResultLeadId,
                existing.LastResultCode);
        }

        if (existing.Status == InboxStatus.Processed)
        {
            return new InboxAdmission(
                InboxAdmissionKind.Replay,
                existing.InboxMessageId,
                existing.ResultLeadId,
                existing.LastResultCode);
        }

        existing.Resume(delivery.CorrelationId, delivery.ReceivedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new InboxAdmission(
            InboxAdmissionKind.Resume,
            existing.InboxMessageId,
            existing.ResultLeadId,
            existing.LastResultCode);
    }
}
