using UnicoreCRM.BuildingBlocks;

namespace UnicoreCRM.Sales.Quotes.Contracts;

public sealed record DealCommitmentBinding(string WorkspaceId, string QuoteId, string AcceptanceId, string OrderId,
    string DealId, string BuyerType, string BuyerId, string CommercialFingerprint, long SourceQuoteVersion,
    string AcceptanceJson);
public interface IDealQuoteCommitmentBinding
{
    Task<bool> VerifyAsync(OwnerTransaction transaction, DealCommitmentBinding source, CancellationToken cancellationToken);
}
