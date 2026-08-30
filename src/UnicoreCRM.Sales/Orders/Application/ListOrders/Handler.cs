using System.Text.RegularExpressions;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Orders.Application.Common;
using UnicoreCRM.Sales.Orders.Contracts;

namespace UnicoreCRM.Sales.Orders.Application.ListOrders;

internal sealed record Query(
    string? Cursor,
    int? Limit,
    string? Search,
    string? SortBy,
    string? SortDirection,
    string? State,
    string? SourceQuoteId,
    string? SourceDealId,
    string? BuyerType,
    string? BuyerId,
    OrderRequestMetadata Metadata);

internal sealed partial class Handler(
    OrderAuthorization authorization,
    IOrdersPersistence persistence)
{
    internal async Task<OrderOperationResult<OrderListResponse>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return OrderOperationResult<OrderListResponse>.Failure(access.Error!);

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        OrderListCursor.TryParse(query.Cursor, fields, out var offset);
        var limit = query.Limit ?? 50;
        if (limit is < 1 or > 250)
            fields["limit"] = ["limit must be between 1 and 250."];
        var search = OptionalSearch(query.Search, fields);
        var sortBy = query.SortBy ?? "updatedAt";
        if (sortBy is not ("updatedAt" or "createdAt" or "orderDate" or "grandTotal" or "orderNumber"))
            fields["sortBy"] = ["sortBy is invalid."];
        var descending = (query.SortDirection ?? "desc") switch
        {
            "asc" => false,
            "desc" => true,
            _ => InvalidDirection(fields)
        };
        if (query.State is not null && query.State is not ("DRAFT" or "CONFIRMED" or "COMPLETED" or "CANCELLED"))
            fields["state"] = ["state is invalid."];
        OptionalEntityId(query.SourceQuoteId, "sourceQuoteId", fields);
        OptionalEntityId(query.SourceDealId, "sourceDealId", fields);
        if (query.BuyerType is not null && query.BuyerType is not ("CONTACT" or "ORGANIZATION_ACCOUNT"))
            fields["buyerType"] = ["buyerType must be CONTACT or ORGANIZATION_ACCOUNT."];
        OptionalEntityId(query.BuyerId, "buyerId", fields);
        if (fields.Count != 0)
            return OrderOperationResult<OrderListResponse>.Failure(OrderErrors.Validation(fields));

        OrderPage page;
        if (access.Value!.Authorization.ScopeFilter == RecordAccessScopeFilter.Workspace)
        {
            page = await persistence.ReadOrdersAsync(
                access.Value.Trusted.WorkspaceId,
                new OrderListSpecification(
                    offset,
                    limit,
                    search,
                    access.Value.Authorization.CanRead("recipientName"),
                    sortBy,
                    descending,
                    query.State,
                    query.SourceQuoteId,
                    query.SourceDealId,
                    query.BuyerType,
                    query.BuyerId),
                cancellationToken);
        }
        else
        {
            // No authoritative Order owner/team fact exists. OWN, TEAM and CUSTOM therefore fail
            // closed before any Order row is queried or counted.
            page = new OrderPage([], 0);
        }

        var nextOffset = offset + page.Items.Count;
        var hasNextPage = nextOffset < page.TotalCount;
        return OrderOperationResult<OrderListResponse>.Success(new(
            page.Items.Select(item => OrderFieldSecurity.Project(
                OrderProjection.Document(item),
                access.Value.Authorization)).ToArray(),
            new OrderPageInfo(
                hasNextPage,
                hasNextPage ? OrderListCursor.Encode(nextOffset) : null,
                page.TotalCount)));
    }

    private static string? OptionalSearch(string? value, IDictionary<string, string[]> fields)
    {
        if (value is null)
            return null;
        var normalized = value.Trim();
        if (normalized.Length == 0)
            return null;
        if (normalized.Length > 240)
            fields["search"] = ["search must contain at most 240 characters."];
        return normalized;
    }

    private static void OptionalEntityId(string? value, string field, IDictionary<string, string[]> fields)
    {
        if (value is not null && !EntityIdPattern().IsMatch(value))
            fields[field] = [$"{field} must be a valid EntityId."];
    }

    private static bool InvalidDirection(IDictionary<string, string[]> fields)
    {
        fields["sortDirection"] = ["sortDirection must be asc or desc."];
        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
