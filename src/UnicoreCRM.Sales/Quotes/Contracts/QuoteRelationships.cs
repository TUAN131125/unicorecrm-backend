namespace UnicoreCRM.Sales.Quotes.Contracts;

/// <summary>Owner-verified CRM relationships under the current trusted Workspace and access policy.</summary>
public interface IQuoteRelationshipVerifier
{
    Task<bool> VerifyBuyerAsync(QuoteBuyerReference buyer, string requestId, string correlationId,
        CancellationToken cancellationToken);
    Task<IQuoteDealSourceLease> AcquireDealAsync(string dealId, long? expectedVersion, bool requireOpen,
        string requestId, string correlationId, CancellationToken cancellationToken);
}

public sealed record QuoteDealSource(string DealId, QuoteBuyerReference BuyerRef, string? ContactId,
    string? SourceLeadId, string OwnerId, long Version);

/// <summary>
/// Holds the Deal owner's source facts stable until disposed. The caller must keep this lease
/// alive through its Quote commit; disposing it releases the read-only owner transaction.
/// </summary>
public interface IQuoteDealSourceLease : IAsyncDisposable
{
    string? ErrorCode { get; }
    QuoteDealSource? Source { get; }
}
