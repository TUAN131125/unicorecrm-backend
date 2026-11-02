namespace UnicoreCRM.Platform.Workspace.Contracts;

/// <summary>Public issuer fields only. A successful lease holds the saved owner revision through consumer commit.</summary>
public interface IQuoteSellerReader
{
    Task<IQuoteSellerLease> AcquireAsync(string requestId, string correlationId, CancellationToken cancellationToken);
}
public interface IQuoteSellerLease : IAsyncDisposable
{
    QuoteSellerSnapshot? Snapshot { get; }
    string? ErrorCode { get; }
    IReadOnlyList<string> Blockers { get; }
}
public sealed record QuoteSellerSnapshot(string WorkspaceId, long ConfigurationVersion, string DisplayName,
    string LegalName, string Address, string? TaxId, string? Email, string? Phone, string? Website, string? TimeZone = null);
