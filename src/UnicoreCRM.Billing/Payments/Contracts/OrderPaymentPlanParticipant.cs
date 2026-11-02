using UnicoreCRM.BuildingBlocks;
namespace UnicoreCRM.Billing.Payments.Contracts;
public interface IOrderPaymentPlanParticipant
{
    Task<PaymentPlanDocument> EnsureDraftInTransactionAsync(OwnerTransaction tx,string ws,string orderId,string acceptedSnapshotJson,CancellationToken ct);
    Task EnsureDraftAsync(string ws,string orderId,string acceptedSnapshotJson,CancellationToken ct);
    Task<PaymentPlanDocument> ActivateAsync(OwnerTransaction tx,string ws,string orderId,string planId,long expectedVersion,
        string buyerType,string buyerId,string agreementJson,DateTimeOffset now,string commandId,string actor,CancellationToken ct);
}
