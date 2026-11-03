using System.Text.RegularExpressions;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;

namespace UnicoreCRM.Sales.Quotes.Application.GetQuote;

internal sealed record Query(string QuoteId, QuoteRequestMetadata Metadata);

internal sealed partial class Handler(
    QuoteAuthorization authorization,
    IQuotesPersistence persistence,
    TimeProvider timeProvider,
    IQuoteCustomerDecisionPolicyReader? customerPolicy = null,
    IQuoteLifecyclePolicyReader? lifecyclePolicy = null, IWorkspaceTimeZoneReader? timeZones = null,
    IEnumerable<IQuoteRetentionVerifier>? retentionVerifiers = null, QuoteLifecycleClock? lifecycleClock = null, QuoteDocumentCommands? documentCommands = null, QuoteSalesPolicyReader? salesPolicies = null, IQuoteApprovalPolicyReader? approvalPolicies = null, QuoteSalesAcceptanceService? salesAcceptance = null)
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

        var zone = timeZones is null ? null : await timeZones.ReadTimeZoneAsync(quote.WorkspaceId, cancellationToken);
        var document = QuoteFieldSecurity.Project(
            QuoteProjection.Document(quote, customerPolicy?.Read(quote.WorkspaceId), lifecyclePolicy?.Read(quote.WorkspaceId),
                timeZones is null ? null : QuoteValidity.Evaluate(quote.ValidUntil, zone, lifecycleClock?.GetUtcNow(quote.WorkspaceId) ?? timeProvider.GetUtcNow()), QuoteRetentionVerification.Available(retentionVerifiers)),
            access.Value.Authorization);
        if (document.Actions.Revise is { Allowed: true } hint
            && (await persistence.ReadRootRevisionStateAsync(quote.WorkspaceId, quote.RootQuoteId, cancellationToken)).HasWorkingRevision)
            document = document with { Actions = document.Actions with { Revise = hint with { Allowed = false, BlockerCodes = ["QUOTE_REVISION_BLOCKED"] } } };
        if (documentCommands is not null)
            document = document with { Actions = document.Actions with { IssueDocument = await documentCommands.AvailabilityAsync(quote, access.Value, query.Metadata, cancellationToken) } };
        document = document with { OrdinarySales = salesPolicies?.WithoutInternalApproval(quote.WorkspaceId) == true && (approvalPolicies is null || (await approvalPolicies.ReadAsync(quote.WorkspaceId, cancellationToken))?.AlwaysRequireApproval != true) };
        if (salesAcceptance is not null)
        {
            var actions = await salesAcceptance.AvailabilityAsync(quote, query.Metadata, cancellationToken);
            document = document with { Actions = document.Actions with {
                SendGmail = actions.Gmail, RecordSalesAcceptance = actions.Staff, CreateAcceptanceLink = actions.Link } };
        }
        await QuoteReadAudit.RecordResourceAsync(
            persistence,
            access.Value,
            query.Metadata,
            quote.QuoteId,
            quote.ResourceVersion,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return QuoteOperationResult<QuoteReadModel>.Success(document);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
