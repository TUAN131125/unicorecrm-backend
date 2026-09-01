using System.Text.RegularExpressions;
using UnicoreCRM.Sales.Orders.Application.Common;
using UnicoreCRM.Sales.Orders.Contracts;

namespace UnicoreCRM.Sales.Orders.Application.GetOrder;

internal sealed record Query(string OrderId, OrderRequestMetadata Metadata);

internal sealed partial class Handler(
    OrderAuthorization authorization,
    IOrdersPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<OrderOperationResult<OrderReadModel>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return OrderOperationResult<OrderReadModel>.Failure(access.Error!);
        if (!EntityIdPattern().IsMatch(query.OrderId))
            return OrderOperationResult<OrderReadModel>.Failure(OrderErrors.NotFound());

        var order = await persistence.ReadOrderAsync(
            access.Value!.Trusted.WorkspaceId,
            query.OrderId,
            cancellationToken);
        if (order is null)
            return OrderOperationResult<OrderReadModel>.Failure(OrderErrors.NotFound());

        var denied = await authorization.EnforceRecordAsync(
            access.Value,
            order,
            "getOrder",
            query.Metadata,
            cancellationToken);
        if (denied is not null)
            return OrderOperationResult<OrderReadModel>.Failure(denied);

        var document = OrderFieldSecurity.Project(
            OrderProjection.Document(order),
            access.Value.Authorization);
        await OrderReadAudit.RecordResourceAsync(
            persistence,
            access.Value,
            query.Metadata,
            order.OrderId,
            order.ResourceVersion,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return OrderOperationResult<OrderReadModel>.Success(document);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
