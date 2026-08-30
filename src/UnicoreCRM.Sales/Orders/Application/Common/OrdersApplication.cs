using UnicoreCRM.Sales.Orders.Domain;

namespace UnicoreCRM.Sales.Orders.Application.Common;

internal sealed record OrderRequestMetadata(string RequestId, string CorrelationId);

internal sealed record OrderOperationError(
    string Code,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

internal sealed record OrderOperationResult<T>(T? Value, OrderOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static OrderOperationResult<T> Success(T value) => new(value, null);
    internal static OrderOperationResult<T> Failure(OrderOperationError error) => new(default, error);
}

internal sealed record OrderListSpecification(
    int Offset,
    int Limit,
    string? Search,
    bool SearchRecipientName,
    string SortBy,
    bool Descending,
    string? State,
    string? SourceQuoteId,
    string? SourceDealId,
    string? BuyerType,
    string? BuyerId);

internal sealed record OrderPage(IReadOnlyList<Order> Items, int TotalCount);

internal interface IOrdersPersistence
{
    Task<Order?> ReadOrderAsync(string workspaceId, string orderId, CancellationToken cancellationToken);
    Task<OrderPage> ReadOrdersAsync(string workspaceId, OrderListSpecification specification, CancellationToken cancellationToken);
}

internal static class OrderErrors
{
    internal static OrderOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static OrderOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static OrderOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static OrderOperationError Validation(IReadOnlyDictionary<string, string[]> fields, int status = 422) =>
        new("VALIDATION_FAILED", status, "Validation failed", FieldErrors: fields);
}
