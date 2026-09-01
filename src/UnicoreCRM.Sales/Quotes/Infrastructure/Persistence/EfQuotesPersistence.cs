using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class EfQuotesPersistence(QuotesDbContext dbContext) : IQuotesPersistence
{
    private const string CaseInsensitiveSearchCollation = "Latin1_General_100_CI_AS";

    public Task<Quote?> ReadQuoteAsync(
        string workspaceId,
        string quoteId,
        CancellationToken cancellationToken) =>
        dbContext.Quotes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId && item.QuoteId == quoteId,
                cancellationToken);

    public async Task<QuotePage> ReadQuotesAsync(
        string workspaceId,
        QuoteListSpecification specification,
        CancellationToken cancellationToken)
    {
        IQueryable<Quote> query = dbContext.Quotes
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId);

        if (specification.Search is { } search)
        {
            query = query.Where(item =>
                EF.Functions.Collate(item.QuoteNumber, CaseInsensitiveSearchCollation).Contains(search)
                || EF.Functions.Collate(item.Title, CaseInsensitiveSearchCollation).Contains(search));
        }
        if (specification.Status is { } status)
            query = query.Where(item => item.Status == status);
        if (specification.SourceDealId is { } sourceDealId)
            query = query.Where(item => item.SourceDealId == sourceDealId);
        if (specification.BuyerType is { } buyerType)
            query = query.Where(item => item.BuyerType == buyerType);
        if (specification.BuyerId is { } buyerId)
            query = query.Where(item => item.BuyerId == buyerId);

        var totalCount = await query.CountAsync(cancellationToken);
        var ordered = Order(query, specification.SortBy, specification.Descending);
        var items = await ordered
            .Skip(specification.Offset)
            .Take(specification.Limit)
            .ToArrayAsync(cancellationToken);
        return new QuotePage(items, totalCount);
    }

    public void AddReadAudit(QuoteReadAuditRecord readAudit) => dbContext.ReadAuditRecords.Add(readAudit);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    private static IOrderedQueryable<Quote> Order(IQueryable<Quote> query, string sortBy, bool descending) =>
        (sortBy, descending) switch
        {
            ("createdAt", true) => query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.QuoteId),
            ("createdAt", false) => query.OrderBy(item => item.CreatedAt).ThenBy(item => item.QuoteId),
            ("validUntil", true) => query.OrderByDescending(item => item.ValidUntil).ThenByDescending(item => item.QuoteId),
            ("validUntil", false) => query.OrderBy(item => item.ValidUntil).ThenBy(item => item.QuoteId),
            ("grandTotal", true) => query.OrderByDescending(item => item.GrandTotalAmount).ThenByDescending(item => item.QuoteId),
            ("grandTotal", false) => query.OrderBy(item => item.GrandTotalAmount).ThenBy(item => item.QuoteId),
            ("quoteNumber", true) => query.OrderByDescending(item => item.QuoteNumber).ThenByDescending(item => item.QuoteId),
            ("quoteNumber", false) => query.OrderBy(item => item.QuoteNumber).ThenBy(item => item.QuoteId),
            (_, true) => query.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.QuoteId),
            _ => query.OrderBy(item => item.UpdatedAt).ThenBy(item => item.QuoteId)
        };
}
