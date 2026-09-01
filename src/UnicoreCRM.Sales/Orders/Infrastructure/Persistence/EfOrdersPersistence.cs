using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Orders.Application.Common;
using UnicoreCRM.Sales.Orders.Domain;

namespace UnicoreCRM.Sales.Orders.Infrastructure.Persistence;

internal sealed class EfOrdersPersistence(OrdersDbContext dbContext) : IOrdersPersistence
{
    private const string CaseInsensitiveSearchCollation = "Latin1_General_100_CI_AS";

    public Task<Order?> ReadOrderAsync(
        string workspaceId,
        string orderId,
        CancellationToken cancellationToken) =>
        dbContext.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId && item.OrderId == orderId,
                cancellationToken);

    public async Task<OrderPage> ReadOrdersAsync(
        string workspaceId,
        OrderListSpecification specification,
        CancellationToken cancellationToken)
    {
        IQueryable<Order> query = dbContext.Orders
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId);

        if (specification.Search is { } search)
        {
            query = specification.SearchRecipientName
                ? query.Where(item =>
                    EF.Functions.Collate(item.OrderNumber, CaseInsensitiveSearchCollation).Contains(search)
                    || (item.RecipientName != null
                        && EF.Functions.Collate(item.RecipientName, CaseInsensitiveSearchCollation).Contains(search)))
                : query.Where(item =>
                    EF.Functions.Collate(item.OrderNumber, CaseInsensitiveSearchCollation).Contains(search));
        }
        if (specification.State is { } state)
            query = query.Where(item => item.State == state);
        if (specification.SourceQuoteId is { } sourceQuoteId)
            query = query.Where(item => item.SourceQuoteId == sourceQuoteId);
        if (specification.SourceDealId is { } sourceDealId)
            query = query.Where(item => item.SourceDealId == sourceDealId);
        if (specification.BuyerType is { } buyerType)
            query = query.Where(item => item.BuyerType == buyerType);
        if (specification.BuyerId is { } buyerId)
            query = query.Where(item => item.BuyerId == buyerId);

        var totalCount = await query.CountAsync(cancellationToken);
        query = Continue(query, specification.SortBy, specification.Descending, specification.Continuation);
        var window = await Order(query, specification.SortBy, specification.Descending)
            .Take(specification.Limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasNextPage = window.Length > specification.Limit;
        var items = hasNextPage ? window[..specification.Limit] : window;
        return new OrderPage(items, totalCount, hasNextPage);
    }

    public void AddReadAudit(OrderReadAuditRecord readAudit) => dbContext.ReadAuditRecords.Add(readAudit);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private static IQueryable<Order> Continue(
        IQueryable<Order> query,
        string sortBy,
        bool descending,
        OrderListContinuation? continuation) => (sortBy, descending, continuation) switch
        {
            (_, _, null) => query,
            ("updatedAt", true, OrderTimestampContinuation value) => query.Where(item =>
                item.UpdatedAt < value.LastPrimary
                || (item.UpdatedAt == value.LastPrimary && string.Compare(item.OrderId, value.LastOrderId) < 0)),
            ("updatedAt", false, OrderTimestampContinuation value) => query.Where(item =>
                item.UpdatedAt > value.LastPrimary
                || (item.UpdatedAt == value.LastPrimary && string.Compare(item.OrderId, value.LastOrderId) > 0)),
            ("createdAt", true, OrderTimestampContinuation value) => query.Where(item =>
                item.CreatedAt < value.LastPrimary
                || (item.CreatedAt == value.LastPrimary && string.Compare(item.OrderId, value.LastOrderId) < 0)),
            ("createdAt", false, OrderTimestampContinuation value) => query.Where(item =>
                item.CreatedAt > value.LastPrimary
                || (item.CreatedAt == value.LastPrimary && string.Compare(item.OrderId, value.LastOrderId) > 0)),
            ("orderDate", true, OrderDateContinuation value) => query.Where(item =>
                item.OrderDate < value.LastPrimary
                || (item.OrderDate == value.LastPrimary && string.Compare(item.OrderId, value.LastOrderId) < 0)),
            ("orderDate", false, OrderDateContinuation value) => query.Where(item =>
                item.OrderDate > value.LastPrimary
                || (item.OrderDate == value.LastPrimary && string.Compare(item.OrderId, value.LastOrderId) > 0)),
            ("grandTotal", true, OrderAmountContinuation value) => query.Where(item =>
                item.GrandTotalAmount < value.LastPrimary
                || (item.GrandTotalAmount == value.LastPrimary && string.Compare(item.OrderId, value.LastOrderId) < 0)),
            ("grandTotal", false, OrderAmountContinuation value) => query.Where(item =>
                item.GrandTotalAmount > value.LastPrimary
                || (item.GrandTotalAmount == value.LastPrimary && string.Compare(item.OrderId, value.LastOrderId) > 0)),
            ("orderNumber", true, OrderTextContinuation value) => query.Where(item =>
                string.Compare(item.OrderNumber, value.LastPrimary) < 0
                || (item.OrderNumber == value.LastPrimary && string.Compare(item.OrderId, value.LastOrderId) < 0)),
            ("orderNumber", false, OrderTextContinuation value) => query.Where(item =>
                string.Compare(item.OrderNumber, value.LastPrimary) > 0
                || (item.OrderNumber == value.LastPrimary && string.Compare(item.OrderId, value.LastOrderId) > 0)),
            _ => throw new InvalidOperationException("The Order continuation does not match its resolved sort field.")
        };

    private static IOrderedQueryable<Order> Order(IQueryable<Order> query, string sortBy, bool descending) =>
        (sortBy, descending) switch
        {
            ("createdAt", true) => query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.OrderId),
            ("createdAt", false) => query.OrderBy(item => item.CreatedAt).ThenBy(item => item.OrderId),
            ("orderDate", true) => query.OrderByDescending(item => item.OrderDate).ThenByDescending(item => item.OrderId),
            ("orderDate", false) => query.OrderBy(item => item.OrderDate).ThenBy(item => item.OrderId),
            ("grandTotal", true) => query.OrderByDescending(item => item.GrandTotalAmount).ThenByDescending(item => item.OrderId),
            ("grandTotal", false) => query.OrderBy(item => item.GrandTotalAmount).ThenBy(item => item.OrderId),
            ("orderNumber", true) => query.OrderByDescending(item => item.OrderNumber).ThenByDescending(item => item.OrderId),
            ("orderNumber", false) => query.OrderBy(item => item.OrderNumber).ThenBy(item => item.OrderId),
            (_, true) => query.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.OrderId),
            _ => query.OrderBy(item => item.UpdatedAt).ThenBy(item => item.OrderId)
        };
}
