using System.Text.RegularExpressions;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;

namespace UnicoreCRM.Sales.Quotes.Application.GetQuote;

internal sealed record Query(string QuoteId, QuoteRequestMetadata Metadata);

internal sealed partial class Handler(
    QuoteAuthorization authorization,
    IQuotesPersistence persistence)
{
    internal async Task<QuoteOperationResult<QuoteReadModel>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return QuoteOperationResult<QuoteReadModel>.Failure(access.Error!);
        if (!EntityIdPattern().IsMatch(query.QuoteId))
            return QuoteOperationResult<QuoteReadModel>.Failure(QuoteErrors.NotFound());

        var quote = await persistence.ReadQuoteAsync(
            access.Value!.Trusted.WorkspaceId,
            query.QuoteId,
            cancellationToken);
        if (quote is null)
            return QuoteOperationResult<QuoteReadModel>.Failure(QuoteErrors.NotFound());

        var denied = await authorization.EnforceRecordAsync(
            access.Value,
            quote,
            "getQuote",
            query.Metadata,
            cancellationToken);
        if (denied is not null)
            return QuoteOperationResult<QuoteReadModel>.Failure(denied);

        return QuoteOperationResult<QuoteReadModel>.Success(
            QuoteFieldSecurity.Project(
                QuoteProjection.Document(quote),
                access.Value.Authorization));
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
