using UnicoreCRM.Sales.Orders.Domain;

namespace UnicoreCRM.Sales.Orders.Application.Common;

/// <summary>
/// Persists exactly one Orders-owned evidence row after a response representation has been
/// materialized and before the successful result can be returned.
/// </summary>
internal static class OrderReadAudit
{
    internal static Task RecordListAsync(
        IOrdersPersistence persistence,
        OrderAccess access,
        OrderRequestMetadata metadata,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        AppendAsync(persistence, access, metadata, "listOrders", null, null, occurredAt, cancellationToken);

    internal static Task RecordResourceAsync(
        IOrdersPersistence persistence,
        OrderAccess access,
        OrderRequestMetadata metadata,
        string recordId,
        long resourceVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken) =>
        AppendAsync(
            persistence,
            access,
            metadata,
            "getOrder",
            recordId,
            resourceVersion,
            occurredAt,
            cancellationToken);

    private static async Task AppendAsync(
        IOrdersPersistence persistence,
        OrderAccess access,
        OrderRequestMetadata metadata,
        string operation,
        string? recordId,
        long? resourceVersion,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        persistence.AddReadAudit(new OrderReadAuditRecord(
            operation,
            access.Trusted.WorkspaceId,
            access.Trusted.MemberId,
            recordId,
            metadata.RequestId,
            metadata.CorrelationId,
            resourceVersion,
            occurredAt));
        await persistence.SaveChangesAsync(cancellationToken);
    }
}
