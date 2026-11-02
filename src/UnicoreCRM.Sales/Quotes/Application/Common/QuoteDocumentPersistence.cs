using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal interface IQuoteDocumentPersistence
{
    Task<QuoteDocument?> FindAsync(string workspaceId, string quoteId, string documentId, CancellationToken ct);
    Task<QuoteDocument?> FindBindingAsync(string workspaceId, string quoteId, string binding, CancellationToken ct);
    Task<int> NextVersionAsync(string workspaceId, string quoteId, CancellationToken ct);
    Task<IReadOnlyList<QuoteDocument>> ListAsync(string workspaceId, string quoteId, int offset, int limit, CancellationToken ct);
    Task<byte[]?> ReadBytesAsync(string workspaceId, string documentId, CancellationToken ct);
    void Add(QuoteDocument document, byte[] bytes);
}
