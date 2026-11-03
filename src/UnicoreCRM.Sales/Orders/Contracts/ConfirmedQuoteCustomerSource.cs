namespace UnicoreCRM.Sales.Orders.Contracts;

/// <summary>System workflow proof, not a user read API. No caller-selected buyer or owner.</summary>
public sealed record ConfirmedQuoteCustomerSource(string WorkspaceId,string OrderId,string QuoteId,
    string AcceptanceId,string BuyerType,string BuyerId,string? OwnerId);
public interface IConfirmedQuoteCustomerSourceReader
{
    Task<ConfirmedQuoteCustomerSource?> ReadAsync(string workspaceId,string orderId,CancellationToken cancellationToken);
}
