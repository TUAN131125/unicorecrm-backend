namespace UnicoreCRM.Sales.Quotes.Contracts;

public interface IQuoteDocumentPartyReader
{
    string BuyerType { get; }
    Task<IQuoteDocumentPartyLease> AcquireAsync(string buyerId, string requestId, string correlationId, CancellationToken cancellationToken);
}
public interface IQuoteDocumentPartyLease : IAsyncDisposable
{
    QuoteDocumentPartySnapshot? Snapshot { get; }
}
/// <summary>Owner-authorized identity/contact capture. The same field rights are required for historical downloads.</summary>
public sealed record QuoteDocumentPartySnapshot(string Type, string Id, long Version, string DisplayName,
    string? Email, string? Phone, bool Eligible);
