using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed record QuoteRequestMetadata(string RequestId, string CorrelationId);
internal sealed record QuoteCommandMetadata(string RequestId, string CorrelationId, string IdempotencyKey, long? ExpectedVersion);

internal sealed record QuoteOperationError(
    string Code,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

internal sealed record QuoteOperationResult<T>(T? Value, QuoteOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static QuoteOperationResult<T> Success(T value) => new(value, null);
    internal static QuoteOperationResult<T> Failure(QuoteOperationError error) => new(default, error);
}

internal sealed record QuoteListSpecification(
    int Offset,
    int Limit,
    string? Search,
    string SortBy,
    bool Descending,
    string? Status,
    string? SourceDealId,
    string? BuyerType,
    string? BuyerId,
    string? ScopeOwnerId = null,
    string? RootQuoteId = null,
    string Archived = "ACTIVE");

internal sealed record QuoteRootRevisionState(int MaximumRevision, bool HasWorkingRevision);

internal sealed record QuotePage(IReadOnlyList<Quote> Items, int TotalCount);

internal interface IQuotesPersistence
{
    Task<IQuoteTransaction> BeginAsync(CancellationToken cancellationToken);
    Task LockAsync(string key, CancellationToken cancellationToken);
    Task<Quote?> LoadQuoteAsync(string workspaceId, string quoteId, CancellationToken cancellationToken);
    Task<QuoteCommandRecord?> FindCommandAsync(string scopeKey, CancellationToken cancellationToken);
    Task<QuoteCustomerDecisionRecord?> FindCustomerDecisionAsync(string workspaceId, string decisionId, CancellationToken cancellationToken);
    Task<string?> ReadCreatorAsync(string workspaceId, string quoteId, CancellationToken cancellationToken);
    Task<QuoteRootRevisionState> ReadRootRevisionStateAsync(string workspaceId, string rootQuoteId, CancellationToken cancellationToken);
    Task<string> NextNumberAsync(CancellationToken cancellationToken);
    void AddQuote(Quote quote);
    void AddCommand(QuoteCommandRecord record);
    void AddOutbox(QuoteOutboxMessage message);
    Task<Quote?> ReadQuoteAsync(string workspaceId, string quoteId, CancellationToken cancellationToken);
    Task<QuotePage> ReadQuotesAsync(string workspaceId, QuoteListSpecification specification, CancellationToken cancellationToken);
    void AddReadAudit(QuoteReadAuditRecord readAudit);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
internal interface IQuoteTransaction : IAsyncDisposable { Task CommitAsync(CancellationToken cancellationToken); }
internal sealed class QuotePersistenceConcurrencyException(Exception inner) : Exception("Quote changed concurrently.", inner);

internal static class QuoteErrors
{
    internal static QuoteOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static QuoteOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static QuoteOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static QuoteOperationError Validation(IReadOnlyDictionary<string, string[]> fields, int status = 422) =>
        new("VALIDATION_FAILED", status, "Validation failed", FieldErrors: fields);
}
