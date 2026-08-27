using System.Numerics;
using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.GetDealForecastSummary;

internal sealed record Query(
    string? OwnerId,
    string? BuyerType,
    string? BuyerId,
    string RequestId,
    string CorrelationId);

internal sealed class Handler(
    DealAuthorization authorization,
    IDealsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<DealOperationResult<DealForecastSummaryReadModel>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var metadata = new DealRequestMetadata(query.RequestId, query.CorrelationId);
        var access = await authorization.AuthorizeAsync(DealCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
            return DealOperationResult<DealForecastSummaryReadModel>.Failure(access.Error!);
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        DealValidation.OptionalEntity(query.OwnerId, "ownerId", fields);
        if (query.BuyerType is not null && query.BuyerType is not ("CONTACT" or "ORGANIZATION_ACCOUNT"))
            fields["buyerType"] = ["buyerType must be CONTACT or ORGANIZATION_ACCOUNT."];
        DealValidation.OptionalEntity(query.BuyerId, "buyerId", fields);
        if (fields.Count != 0)
            return DealOperationResult<DealForecastSummaryReadModel>.Failure(DealErrors.Validation(fields));

        var trusted = access.Value!.Trusted;

        // The forecast aggregates deal amounts, so an out-of-scope deal must not reach the totals.
        // The scope is pushed into the query rather than filtered afterwards, which is what keeps a
        // hidden deal out of the aggregate as well as out of any list.
        var scope = access.Value!.Authorization.ScopeFilter;
        if (scope == RecordAccessScopeFilter.Denied)
        {
            return DealOperationResult<DealForecastSummaryReadModel>.Success(
                new DealForecastSummaryReadModel(DealProjection.Utc(timeProvider.GetUtcNow()), [], true));
        }

        var allDeals = await persistence.ReadDealsAsync(
            trusted.WorkspaceId,
            scope == RecordAccessScopeFilter.OwnedByMember ? access.Value!.Authorization.ScopeOwnerMemberId : null,
            cancellationToken);
        var deals = allDeals.Where(deal =>
            !deal.IsArchived
            && deal.StageCategory == DealStageCategory.Open
            && (query.OwnerId is null || deal.Profile.OwnerId == query.OwnerId)
            && (query.BuyerType is null || deal.Profile.BuyerRef.Type == query.BuyerType)
            && (query.BuyerId is null || deal.Profile.BuyerRef.Id == query.BuyerId));
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var buckets = deals
            .GroupBy(deal => deal.Profile.Amount.Currency, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => Bucket(group.Key, group, today))
            .ToArray();
        persistence.AddAudit(new DealAuditRecord(
            "getDealForecastSummary",
            trusted.WorkspaceId,
            trusted.MemberId,
            null,
            query.RequestId,
            query.CorrelationId,
            "READ",
            null,
            null,
            now));
        await persistence.SaveChangesAsync(cancellationToken);
        return DealOperationResult<DealForecastSummaryReadModel>.Success(
            new DealForecastSummaryReadModel(DealProjection.Utc(now), buckets, true));
    }

    private static DealForecastCurrencyBucket Bucket(string currency, IEnumerable<Deal> deals, DateOnly today)
    {
        var values = deals.ToArray();
        BigInteger open = 0;
        BigInteger commit = 0;
        BigInteger bestCase = 0;
        BigInteger pipeline = 0;
        BigInteger weighted = 0;
        foreach (var deal in values)
        {
            var amount = DealDecimal.ParseScaled(deal.Profile.Amount.Amount);
            open += amount;
            switch (deal.ForecastCategory)
            {
                case DealForecastCategory.Commit: commit += amount; break;
                case DealForecastCategory.BestCase: bestCase += amount; break;
                default: pipeline += amount; break;
            }
            weighted += DealDecimal.PercentageOf(amount, DealDecimal.ParseScaled(deal.Profile.OpportunityScore));
        }

        return new DealForecastCurrencyBucket(
            currency,
            values.Length,
            values.Count(deal => deal.Profile.ExpectedCloseDate < today),
            values.Count(deal => deal.Profile.ExpectedCloseDate.Year == today.Year && deal.Profile.ExpectedCloseDate.Month == today.Month),
            Money(open, currency),
            Money(commit, currency),
            Money(bestCase, currency),
            Money(pipeline, currency),
            Money(weighted, currency));
    }

    private static DealMoney Money(BigInteger amount, string currency) => new(DealDecimal.Format(amount), currency);
}
