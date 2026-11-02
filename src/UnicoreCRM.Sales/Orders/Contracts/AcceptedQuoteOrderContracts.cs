namespace UnicoreCRM.Sales.Orders.Contracts;

public sealed record AcceptedQuoteOrderDraft(string WorkspaceId,string QuoteId,string AcceptanceId,string SnapshotJson,
    string CommercialFingerprint,string AcceptanceJson,DateTimeOffset AcceptedAt);
public interface IAcceptedQuoteOrderWriter
{
    Task<OrderConfirmationContext> CreateInTransactionAsync(UnicoreCRM.BuildingBlocks.OwnerTransaction tx,AcceptedQuoteOrderDraft source,CancellationToken ct);
    Task<string> CreateAsync(AcceptedQuoteOrderDraft source,CancellationToken ct);
}
