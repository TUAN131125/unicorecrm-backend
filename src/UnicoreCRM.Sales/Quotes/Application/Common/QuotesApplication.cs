using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed record QuoteRequestMetadata(string RequestId, string CorrelationId);

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
    string? BuyerId);

internal sealed record QuotePage(IReadOnlyList<Quote> Items, int TotalCount);

internal interface IQuotesPersistence
{
    Task<Quote?> ReadQuoteAsync(string workspaceId, string quoteId, CancellationToken cancellationToken);
    Task<QuotePage> ReadQuotesAsync(string workspaceId, QuoteListSpecification specification, CancellationToken cancellationToken);
    void AddReadAudit(QuoteReadAuditRecord readAudit);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal static class QuoteErrors
{
    internal static QuoteOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static QuoteOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static QuoteOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static QuoteOperationError Validation(IReadOnlyDictionary<string, string[]> fields, int status = 422) =>
        new("VALIDATION_FAILED", status, "Validation failed", FieldErrors: fields);
}
