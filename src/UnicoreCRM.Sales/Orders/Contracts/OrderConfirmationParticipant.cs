using UnicoreCRM.BuildingBlocks;
using System.Text.Json.Serialization;
namespace UnicoreCRM.Sales.Orders.Contracts;

public sealed record OrderConfirmationContext(string WorkspaceId,string MemberId,OrderReadModel Order,string AcceptanceId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrderConfirmationFulfillment(string RecipientName,string RecipientPhone,OrderShippingAddressReadModel ShippingAddress);
public interface IOrderConfirmationParticipant
{
    Task<OrderConfirmationContext> ReadAsync(OwnerTransaction tx,string orderId,string requestId,string correlationId,CancellationToken ct);
    Task ValidateSnapshotAsync(OrderConfirmationContext order,string acceptedSnapshotJson,CancellationToken ct);
    Task<long> ConfirmAutomaticallyAsync(OrderConfirmationContext order,string commandId,string policyVersion,DateTimeOffset now,CancellationToken ct);
    Task<long> ConfirmAsync(OrderConfirmationContext order,string commandId,string reviewNotes,OrderConfirmationFulfillment? fulfillment,DateTimeOffset now,CancellationToken ct);
}
