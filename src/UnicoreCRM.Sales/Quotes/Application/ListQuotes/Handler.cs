using System.Text.RegularExpressions;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;

namespace UnicoreCRM.Sales.Quotes.Application.ListQuotes;

internal sealed record Query(
    string? Cursor,
    int? Limit,
    string? Search,
    string? SortBy,
    string? SortDirection,
    string? Status,
    string? SourceDealId,
    string? BuyerType,
    string? BuyerId,
    QuoteRequestMetadata Metadata,
    string? RootQuoteId = null,
    string? Archived = null);

internal sealed partial class Handler(
    QuoteAuthorization authorization,
    IQuotesPersistence persistence,
    TimeProvider timeProvider,
    IQuoteCustomerDecisionPolicyReader? customerPolicy = null,
    IQuoteLifecyclePolicyReader? lifecyclePolicy = null, IWorkspaceTimeZoneReader? timeZones = null,
    IEnumerable<IQuoteRetentionVerifier>? retentionVerifiers = null, QuoteLifecycleClock? lifecycleClock = null)
{
    internal async Task<QuoteOperationResult<QuoteListResponse>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return QuoteOperationResult<QuoteListResponse>.Failure(access.Error!);

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        QuoteListCursor.TryParse(query.Cursor, fields, out var offset);
        var limit = query.Limit ?? 50;
        if (limit is < 1 or > 250)
            fields["limit"] = ["limit must be between 1 and 250."];
        var search = OptionalSearch(query.Search, fields);
        // OpenAPI declares the allowed sort fields/directions, but no defaults. These fallbacks
        // exist only to keep unsorted pagination deterministic and are not a wire-contract promise.
        var sortBy = query.SortBy ?? "updatedAt";
        if (sortBy is not ("updatedAt" or "createdAt" or "validUntil" or "grandTotal" or "quoteNumber" or "quoteRevision"))
            fields["sortBy"] = ["sortBy is invalid."];
        var descending = (query.SortDirection ?? "desc") switch
        {
            "asc" => false,
            "desc" => true,
            _ => InvalidDirection(fields)
        };
        if (query.Status is not null && query.Status is not ("DRAFT" or "REVIEW" or "SENT" or "ACCEPTED" or "REJECTED" or "EXPIRED"))
            fields["status"] = ["status is invalid."];
        OptionalEntityId(query.SourceDealId, "sourceDealId", fields);
        if (query.BuyerType is not null && query.BuyerType is not ("CONTACT" or "ORGANIZATION_ACCOUNT"))
            fields["buyerType"] = ["buyerType must be CONTACT or ORGANIZATION_ACCOUNT."];
        OptionalEntityId(query.BuyerId, "buyerId", fields);
        OptionalEntityId(query.RootQuoteId, "rootQuoteId", fields);
        if (query.Archived is not null and not ("ACTIVE" or "ARCHIVED" or "ALL")) fields["archived"] = ["archived must be ACTIVE, ARCHIVED or ALL."];
        if (query.RootQuoteId is not null && !access.Value!.Authorization.CanRead("rootQuoteId")
            || !access.Value!.Authorization.CanRead("archivedAt"))
            return QuoteOperationResult<QuoteListResponse>.Failure(QuoteErrors.AccessDenied());
        if (fields.Count != 0)
            return QuoteOperationResult<QuoteListResponse>.Failure(QuoteErrors.Validation(fields));

        QuotePage page;
        if (access.Value!.Authorization.ScopeFilter == RecordAccessScopeFilter.Workspace
            || access.Value.Authorization.ScopeFilter == RecordAccessScopeFilter.OwnedByMember && access.Value.Authorization.ScopeOwnerMemberId is not null)
        {
            page = await persistence.ReadQuotesAsync(
                access.Value.Trusted.WorkspaceId,
                new QuoteListSpecification(
                    offset,
                    limit,
                    search,
                    sortBy,
                    descending,
                    query.Status,
                    query.SourceDealId,
                    query.BuyerType,
                    query.BuyerId,
                    access.Value.Authorization.ScopeFilter == RecordAccessScopeFilter.OwnedByMember ? access.Value.Authorization.ScopeOwnerMemberId : null, query.RootQuoteId, query.Archived ?? "ACTIVE"),
                cancellationToken);
        }
        else
        {
            // Quote scope ownership is not authoritative. OWN, TEAM and CUSTOM therefore fail
            // closed before any Quote row is queried or counted.
            page = new QuotePage([], 0);
        }

        var nextOffset = offset + page.Items.Count;
        var hasNextPage = nextOffset < page.TotalCount;
        var decisionPolicy = customerPolicy?.Read(access.Value.Trusted.WorkspaceId);
        var b4Policy = lifecyclePolicy?.Read(access.Value.Trusted.WorkspaceId);
        var zone = timeZones is null ? null : await timeZones.ReadTimeZoneAsync(access.Value.Trusted.WorkspaceId, cancellationToken);
        var now = lifecycleClock?.GetUtcNow(access.Value.Trusted.WorkspaceId) ?? timeProvider.GetUtcNow();
        var response = new QuoteListResponse(
            page.Items.Select(item => QuoteFieldSecurity.Project(
                QuoteProjection.Document(item, decisionPolicy, b4Policy, timeZones is null ? null : QuoteValidity.Evaluate(item.ValidUntil, zone, now), QuoteRetentionVerification.Available(retentionVerifiers)),
                access.Value.Authorization)).ToArray(),
            new QuotePageInfo(
                hasNextPage,
                hasNextPage ? QuoteListCursor.Encode(nextOffset) : null,
                page.TotalCount));
        await QuoteReadAudit.RecordListAsync(
            persistence,
            access.Value,
            query.Metadata,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return QuoteOperationResult<QuoteListResponse>.Success(response);
    }

    private static string? OptionalSearch(string? value, IDictionary<string, string[]> fields)
    {
        if (value is null)
            return null;
        var normalized = value.Trim();
        if (normalized.Length == 0)
            return null;
        if (normalized.Length > 240)
            fields["search"] = ["search must contain at most 240 characters."];
        return normalized;
    }

    private static void OptionalEntityId(string? value, string field, IDictionary<string, string[]> fields)
    {
        if (value is not null && !EntityIdPattern().IsMatch(value))
            fields[field] = [$"{field} must be a valid EntityId."];
    }

    private static bool InvalidDirection(IDictionary<string, string[]> fields)
    {
        fields["sortDirection"] = ["sortDirection must be asc or desc."];
        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
