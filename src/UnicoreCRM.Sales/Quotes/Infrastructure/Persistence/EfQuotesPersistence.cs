using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Globalization;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class EfQuotesPersistence(QuotesDbContext dbContext) : IQuotesPersistence
{
    public async Task<IQuoteTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new Transaction(await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken));
    public async Task LockAsync(string key, CancellationToken cancellationToken)
    {
        // Entity IDs are ASCII; SQL may compare them case-insensitively. Resource locks must not split on path casing.
        var recordKey = key.StartsWith("aggregate:", StringComparison.Ordinal) || key.StartsWith("root-revision:", StringComparison.Ordinal)
            ? key.ToLowerInvariant() : key;
        var resource = "quote-command:" + recordKey;
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @result < 0 THROW 51001, 'Quote command lock unavailable', 1;", cancellationToken);
    }
    public Task<Quote?> LoadQuoteAsync(string workspaceId, string quoteId, CancellationToken cancellationToken) =>
        dbContext.Quotes.Include(x => x.CustomerDecisions).Include(x=>x.SalesAcceptance).SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.QuoteId == quoteId, cancellationToken);
    public Task<QuoteCustomerDecisionRecord?> FindCustomerDecisionAsync(string workspaceId, string decisionId, CancellationToken cancellationToken) =>
        dbContext.Set<QuoteCustomerDecisionRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.DecisionId == decisionId, cancellationToken);
    public Task<QuoteCommandRecord?> FindCommandAsync(string scopeKey, CancellationToken cancellationToken) =>
        dbContext.CommandRecords.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scopeKey, cancellationToken);
    public async Task<string?> ReadCreatorAsync(string workspaceId, string quoteId, CancellationToken cancellationToken)
    {
        var creators = await dbContext.CommandRecords.AsNoTracking().Where(x => x.WorkspaceId == workspaceId
            && x.QuoteId == quoteId && (x.Operation == "createQuoteCommand" || x.Operation == "createQuoteForDeal" || x.Operation == "reviseQuoteCommand"))
            .Select(x => x.ActorId).Take(2).ToArrayAsync(cancellationToken);
        return creators.Length == 1 ? creators[0] : null;
    }
    public async Task<QuoteRootRevisionState> ReadRootRevisionStateAsync(string workspaceId, string rootQuoteId, CancellationToken ct)
    {
        var rows = dbContext.Quotes.AsNoTracking().Where(q => q.WorkspaceId == workspaceId && q.RootQuoteId == rootQuoteId);
        var maximum = await rows.MaxAsync(q => (int?)q.QuoteRevision, ct) ?? 0;
        var working = await rows.AnyAsync(q => q.ArchivedAt == null && (q.Status == "DRAFT" || q.Status == "REVIEW"), ct);
        return new(maximum, working);
    }
    public async Task<string> NextNumberAsync(CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT NEXT VALUE FOR quotes.QuoteNumbers";
        var value = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return "Q-" + value.ToString("D10", CultureInfo.InvariantCulture);
    }
    public void AddQuote(Quote quote) => dbContext.Quotes.Add(quote);
    public void AddCommand(QuoteCommandRecord record) => dbContext.CommandRecords.Add(record);
    public void AddOutbox(QuoteOutboxMessage message) => dbContext.OutboxMessages.Add(message);
    private const string CaseInsensitiveSearchCollation = "Latin1_General_100_CI_AS";

    public Task<Quote?> ReadQuoteAsync(
        string workspaceId,
        string quoteId,
        CancellationToken cancellationToken) =>
        dbContext.Quotes
            .AsNoTracking()
            .Include(item => item.CustomerDecisions)
            .Include(item => item.SalesAcceptance)
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId && item.QuoteId == quoteId,
                cancellationToken);

    public async Task<QuotePage> ReadQuotesAsync(
        string workspaceId,
        QuoteListSpecification specification,
        CancellationToken cancellationToken)
    {
        IQueryable<Quote> query = dbContext.Quotes
            .Include(item => item.SalesAcceptance)
            .AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId);
        if (specification.ScopeOwnerId is not null) query = query.Where(item => item.OwnerId == specification.ScopeOwnerId);

        if (specification.RootQuoteId is { } rootId) query = query.Where(item => item.RootQuoteId == rootId);
        if (specification.Archived == "ACTIVE") query = query.Where(item => item.ArchivedAt == null);
        else if (specification.Archived == "ARCHIVED") query = query.Where(item => item.ArchivedAt != null);
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

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException error) { throw new QuotePersistenceConcurrencyException(error); }
    }

    private static IOrderedQueryable<Quote> Order(IQueryable<Quote> query, string sortBy, bool descending) =>
        (sortBy, descending) switch
        {
            ("createdAt", true) => query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.QuoteId),
            ("createdAt", false) => query.OrderBy(item => item.CreatedAt).ThenBy(item => item.QuoteId),
            ("validUntil", true) => query.OrderByDescending(item => item.ValidUntil).ThenByDescending(item => item.QuoteId),
            ("validUntil", false) => query.OrderBy(item => item.ValidUntil).ThenBy(item => item.QuoteId),
            ("grandTotal", true) => query.OrderByDescending(item => item.GrandTotalAmount).ThenByDescending(item => item.QuoteId),
            ("grandTotal", false) => query.OrderBy(item => item.GrandTotalAmount).ThenBy(item => item.QuoteId),
            ("quoteRevision", true) => query.OrderByDescending(item => item.QuoteRevision).ThenByDescending(item => item.QuoteId),
            ("quoteRevision", false) => query.OrderBy(item => item.QuoteRevision).ThenBy(item => item.QuoteId),
            ("quoteNumber", true) => query.OrderByDescending(item => item.QuoteNumber).ThenByDescending(item => item.QuoteId),
            ("quoteNumber", false) => query.OrderBy(item => item.QuoteNumber).ThenBy(item => item.QuoteId),
            (_, true) => query.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.QuoteId),
            _ => query.OrderBy(item => item.UpdatedAt).ThenBy(item => item.QuoteId)
        };

    private sealed class Transaction(IDbContextTransaction transaction) : IQuoteTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
