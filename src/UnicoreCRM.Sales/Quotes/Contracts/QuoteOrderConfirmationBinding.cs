using UnicoreCRM.BuildingBlocks;
namespace UnicoreCRM.Sales.Quotes.Contracts;
public sealed record QuoteOrderConfirmationBinding(string SnapshotJson,bool OperationalReviewRequired);
public interface IQuoteOrderConfirmationBinding
{
    Task<AutomaticQuoteOrderBinding> ValidateAutomaticAsync(OwnerTransaction tx,AcceptedQuoteOrderSource source,CancellationToken ct);
    Task<QuoteOrderConfirmationBinding> ValidateAsync(OwnerTransaction tx,string ws,string quoteId,string orderId,
        string acceptanceId,string fingerprint,long sourceVersion,string requestId,string correlationId,CancellationToken ct);
}

public sealed record AutomaticQuoteOrderBinding(QuoteOrderConfirmationBinding Binding,string PolicyVersion);
/// <summary>Internal policy read: only eligibility, no personal fields or caller-supplied authority.</summary>
public interface IQuoteAutomaticBuyerReader
{
    string BuyerType { get; }
    Task<bool> IsEligibleAsync(OwnerTransaction tx,string workspaceId,string buyerId,CancellationToken ct);
}
