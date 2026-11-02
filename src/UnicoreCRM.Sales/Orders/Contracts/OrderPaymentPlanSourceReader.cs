namespace UnicoreCRM.Sales.Orders.Contracts;
public sealed record OrderPaymentPlanSource(string WorkspaceId,string OrderId,string SnapshotJson);
public interface IOrderPaymentPlanSourceReader
{
    Task<IReadOnlyList<OrderPaymentPlanSource>> ReadDraftsAsync(string? after,int limit,CancellationToken ct);
}
