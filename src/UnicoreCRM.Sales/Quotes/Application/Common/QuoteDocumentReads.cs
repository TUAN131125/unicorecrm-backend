using System.Globalization;
using System.Security.Cryptography;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed record QuoteDocumentFile(QuoteIssuedDocument Metadata, byte[] Bytes);
internal sealed record QuoteDocumentReadContext(Quote Quote, QuoteAccess Access);
internal sealed class QuoteDocumentReads(QuoteAuthorization authorization, IQuotesPersistence quotes, IQuoteDocumentPersistence documents,
    QuoteDocumentCommands commands, IQuoteSellerReader seller, TimeProvider clock)
{
    internal async Task<QuoteOperationResult<QuoteDocumentSeller>> SellerAsync(QuoteRequestMetadata metadata, CancellationToken ct)
    {
        var access = await authorization.AuthorizeAsync(metadata, ct);
        if (!access.IsSuccess) return QuoteOperationResult<QuoteDocumentSeller>.Failure(access.Error!);
        await using var lease = await seller.AcquireAsync(metadata.RequestId, metadata.CorrelationId, ct);
        if (lease.ErrorCode is not null) return QuoteOperationResult<QuoteDocumentSeller>.Failure(QuoteErrors.AccessDenied());
        return QuoteOperationResult<QuoteDocumentSeller>.Success(new(lease.Snapshot is not null, lease.Blockers, lease.Snapshot));
    }
    private async Task<QuoteOperationResult<QuoteDocumentReadContext>> ContextAsync(string id, string operation, QuoteRequestMetadata metadata, CancellationToken ct)
    {
        var access = await authorization.AuthorizeAsync(metadata, ct);
        if (!access.IsSuccess) return QuoteOperationResult<QuoteDocumentReadContext>.Failure(access.Error!);
        if (!QuotePricing.EntityId(id)) return QuoteOperationResult<QuoteDocumentReadContext>.Failure(QuoteErrors.NotFound());
        var quote = await quotes.ReadQuoteAsync(access.Value!.Trusted.WorkspaceId, id, ct);
        if (quote is null) return QuoteOperationResult<QuoteDocumentReadContext>.Failure(QuoteErrors.NotFound());
        var error = await authorization.EnforceRecordAsync(access.Value, quote, operation, metadata, ct);
        return error is not null ? QuoteOperationResult<QuoteDocumentReadContext>.Failure(error)
            : QuoteOperationResult<QuoteDocumentReadContext>.Success(new(quote, access.Value));
    }
    internal async Task<QuoteOperationResult<QuoteDocumentList>> ListAsync(string id, string? cursor, int limit, QuoteRequestMetadata metadata, CancellationToken ct)
    {
        var context = await ContextAsync(id, "listQuoteDocuments", metadata, ct);
        if (!context.IsSuccess) return QuoteOperationResult<QuoteDocumentList>.Failure(context.Error!);
        var offset = 0;
        if (limit is < 1 or > 50 || cursor is not null && (!int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset < 0 || offset > int.MaxValue - 51))
            return QuoteOperationResult<QuoteDocumentList>.Failure(QuoteErrors.Validation(new Dictionary<string, string[]> { ["pagination"] = ["Invalid cursor or limit"] }));
        var scope = context.Value!; var rows = await documents.ListAsync(scope.Access.Trusted.WorkspaceId, scope.Quote.QuoteId, offset, limit + 1, ct);
        var result = new List<QuoteIssuedDocument>();
        foreach (var row in rows.Take(limit))
        {
            if (!await commands.CanReadStoredAsync(row, [scope.Access], metadata, ct)) return QuoteOperationResult<QuoteDocumentList>.Failure(QuoteErrors.AccessDenied());
            result.Add(QuoteDocumentCommands.Metadata(row, scope.Quote.QuoteNumber));
        }
        await AuditAsync(scope, "listQuoteDocuments", metadata, ct);
        return QuoteOperationResult<QuoteDocumentList>.Success(new(result, new(rows.Count > limit, rows.Count > limit ? (offset + limit).ToString(CultureInfo.InvariantCulture) : null)));
    }
    internal async Task<QuoteOperationResult<QuoteIssuedDocument>> GetAsync(string quoteId, string documentId, QuoteRequestMetadata metadata, CancellationToken ct)
    {
        var pair = await ReadAsync(quoteId, documentId, "getQuoteDocument", metadata, ct);
        if (!pair.IsSuccess) return QuoteOperationResult<QuoteIssuedDocument>.Failure(pair.Error!);
        await AuditAsync(pair.Value!.Context, "getQuoteDocument", metadata, ct);
        return QuoteOperationResult<QuoteIssuedDocument>.Success(QuoteDocumentCommands.Metadata(pair.Value.Document, pair.Value.Context.Quote.QuoteNumber));
    }
    internal async Task<QuoteOperationResult<QuoteDocumentFile>> DownloadAsync(string quoteId, string documentId, QuoteRequestMetadata metadata, CancellationToken ct)
    {
        var pair = await ReadAsync(quoteId, documentId, "downloadQuoteDocument", metadata, ct);
        if (!pair.IsSuccess) return QuoteOperationResult<QuoteDocumentFile>.Failure(pair.Error!);
        var item = pair.Value!; var data = await documents.ReadBytesAsync(item.Context.Access.Trusted.WorkspaceId, item.Document.DocumentId, ct);
        if (data is null || data.Length != item.Document.FileSize || Convert.ToHexString(SHA256.HashData(data)) != item.Document.PdfSha256)
            return QuoteOperationResult<QuoteDocumentFile>.Failure(new("INTEGRATION_UNAVAILABLE", 503, "Issued document integrity unavailable", "DOCUMENT_CONTENT_INTEGRITY_FAILURE"));
        await AuditAsync(item.Context, "downloadQuoteDocument", metadata, ct);
        return QuoteOperationResult<QuoteDocumentFile>.Success(new(QuoteDocumentCommands.Metadata(item.Document, item.Context.Quote.QuoteNumber), data));
    }
    private async Task<QuoteOperationResult<DocumentContext>> ReadAsync(string quoteId, string documentId, string operation, QuoteRequestMetadata metadata, CancellationToken ct)
    {
        var access = await ContextAsync(quoteId, operation, metadata, ct);
        if (!access.IsSuccess) return QuoteOperationResult<DocumentContext>.Failure(access.Error!);
        if (!QuotePricing.EntityId(documentId)) return QuoteOperationResult<DocumentContext>.Failure(QuoteErrors.NotFound());
        var scope = access.Value!; var doc = await documents.FindAsync(scope.Access.Trusted.WorkspaceId, scope.Quote.QuoteId, documentId, ct);
        if (doc is null) return QuoteOperationResult<DocumentContext>.Failure(QuoteErrors.NotFound());
        return !await commands.CanReadStoredAsync(doc, [scope.Access], metadata, ct)
            ? QuoteOperationResult<DocumentContext>.Failure(QuoteErrors.AccessDenied()) : QuoteOperationResult<DocumentContext>.Success(new(scope, doc));
    }
    private async Task AuditAsync(QuoteDocumentReadContext context, string operation, QuoteRequestMetadata metadata, CancellationToken ct)
    {
        quotes.AddReadAudit(new(operation, context.Access.Trusted.WorkspaceId, context.Access.Trusted.MemberId, context.Quote.QuoteId,
            metadata.RequestId, metadata.CorrelationId, context.Quote.ResourceVersion, clock.GetUtcNow()));
        await quotes.SaveChangesAsync(ct);
    }
    private sealed record DocumentContext(QuoteDocumentReadContext Context, QuoteDocument Document);
}
