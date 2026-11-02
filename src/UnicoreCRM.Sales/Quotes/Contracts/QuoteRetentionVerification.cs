namespace UnicoreCRM.Sales.Quotes.Contracts;

/// <summary>Opaque, owner-authorized verification only; no downstream data or mutation authority.</summary>
public enum QuoteRetentionOwner { Deals, Orders, Payments }
public enum QuoteRetentionEligibility { Clear, Blocked, Unavailable }
public interface IQuoteRetentionVerifier
{
    QuoteRetentionOwner Owner { get; }
    Task<IQuoteRetentionLease> AcquireAsync(string quoteId, string requestId, string correlationId, CancellationToken cancellationToken);
}
/// <summary>Owner-owned read/range protection held until the Quote archive transaction has committed.</summary>
public interface IQuoteRetentionLease : IAsyncDisposable
{
    QuoteRetentionEligibility Eligibility { get; }
}
