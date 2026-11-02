using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class EfQuoteDocumentPersistence(QuotesDbContext db) : IQuoteDocumentPersistence
{
    public Task<QuoteDocument?> FindAsync(string ws, string quote, string id, CancellationToken ct) => db.Set<QuoteDocument>().AsNoTracking()
        .SingleOrDefaultAsync(x => x.WorkspaceId == ws && x.QuoteId == quote && x.DocumentId == id, ct);
    public Task<QuoteDocument?> FindBindingAsync(string ws, string quote, string binding, CancellationToken ct) => db.Set<QuoteDocument>().AsNoTracking()
        .SingleOrDefaultAsync(x => x.WorkspaceId == ws && x.QuoteId == quote && x.BindingHash == binding, ct);
    public async Task<int> NextVersionAsync(string ws, string quote, CancellationToken ct) =>
        (await db.Set<QuoteDocument>().Where(x => x.WorkspaceId == ws && x.QuoteId == quote).MaxAsync(x => (int?)x.DocumentVersion, ct) ?? 0) + 1;
    public async Task<IReadOnlyList<QuoteDocument>> ListAsync(string ws, string quote, int offset, int limit, CancellationToken ct) =>
        await db.Set<QuoteDocument>().AsNoTracking().Where(x => x.WorkspaceId == ws && x.QuoteId == quote)
            .OrderByDescending(x => x.DocumentVersion).Skip(offset).Take(limit).ToArrayAsync(ct);
    public Task<byte[]?> ReadBytesAsync(string ws, string id, CancellationToken ct) => db.Set<QuoteDocumentContent>().AsNoTracking()
        .Where(x => x.WorkspaceId == ws && x.DocumentId == id).Select(x => (byte[]?)x.Bytes).SingleOrDefaultAsync(ct);
    public void Add(QuoteDocument document, byte[] bytes)
    { db.Add(document); db.Add(new QuoteDocumentContent(document.WorkspaceId, document.DocumentId, bytes)); }
}
