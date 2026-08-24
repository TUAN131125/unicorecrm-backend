using System.Numerics;
using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.ListDeals;

internal sealed record Query(
    string? Cursor,
    int? Limit,
    string? Search,
    string? SortBy,
    string? SortDirection,
    string? StageCode,
    string? StageCategory,
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
    internal async Task<DealOperationResult<DealListResponse>> HandleAsync(Query query, CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(DealCapabilities.Read, query.CorrelationId, cancellationToken);
        if (!access.IsSuccess)
            return DealOperationResult<DealListResponse>.Failure(access.Error!);
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        DealValidation.TryCursor(query.Cursor, fields, out var offset);
        var limit = query.Limit ?? 50;
        if (limit is < 1 or > 250)
            fields["limit"] = ["limit must be between 1 and 250."];
        var search = DealValidation.OptionalText(query.Search, "search", 240, fields);
        var sortBy = query.SortBy ?? "updatedAt";
        if (sortBy is not ("updatedAt" or "createdAt" or "expectedCloseDate" or "amount" or "opportunityScore"))
            fields["sortBy"] = ["sortBy is invalid."];
        var descending = (query.SortDirection ?? "desc") switch
        {
            "asc" => false,
            "desc" => true,
            _ => InvalidDirection(fields)
        };
        if (query.StageCode is not null && (query.StageCode.Length is < 1 or > 120))
            fields["stageCode"] = ["stageCode must contain between 1 and 120 characters."];
        var stageCategory = ParseStageCategory(query.StageCategory, fields);
        DealValidation.OptionalEntity(query.OwnerId, "ownerId", fields);
        if (query.BuyerType is not null && query.BuyerType is not ("CONTACT" or "ORGANIZATION_ACCOUNT"))
            fields["buyerType"] = ["buyerType must be CONTACT or ORGANIZATION_ACCOUNT."];
        DealValidation.OptionalEntity(query.BuyerId, "buyerId", fields);
        if (fields.Count != 0)
            return DealOperationResult<DealListResponse>.Failure(DealErrors.Validation(fields));

        var trusted = access.Value!;
        var deals = await persistence.ReadDealsAsync(trusted.WorkspaceId, cancellationToken);
        var filtered = deals.Where(deal =>
            (search is null || Search(deal, search))
            && (query.StageCode is null || deal.StageCode == query.StageCode)
            && (stageCategory is null || deal.StageCategory == stageCategory)
            && (query.OwnerId is null || deal.Profile.OwnerId == query.OwnerId)
            && (query.BuyerType is null || deal.Profile.BuyerRef.Type == query.BuyerType)
            && (query.BuyerId is null || deal.Profile.BuyerRef.Id == query.BuyerId));
        var ordered = Order(filtered, sortBy, descending);
        var materialized = ordered.ToArray();
        var totalCount = materialized.Length;
        var items = materialized.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + items.Length;
        var hasNext = nextOffset < totalCount;
        persistence.AddAudit(new DealAuditRecord(
            "listDeals",
            trusted.WorkspaceId,
            trusted.MemberId,
            null,
            query.RequestId,
            query.CorrelationId,
            "READ",
            null,
            null,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return DealOperationResult<DealListResponse>.Success(new DealListResponse(
            items.Select(DealProjection.Document).ToArray(),
            new DealPageInfo(hasNext, hasNext ? DealValidation.Cursor(nextOffset) : null, totalCount)));
    }

    private static bool Search(Deal deal, string search) =>
        new[] { deal.Profile.Name, deal.Profile.BuyerRef.Id, deal.Profile.ContactId, deal.Profile.SourceLeadId, deal.Profile.Notes }
            .Where(value => value is not null)
            .Any(value => value!.Contains(search, StringComparison.OrdinalIgnoreCase));

    private static IOrderedEnumerable<Deal> Order(IEnumerable<Deal> deals, string sortBy, bool descending)
    {
        Func<Deal, IComparable> key = sortBy switch
        {
            "createdAt" => deal => deal.CreatedAt,
            "expectedCloseDate" => deal => deal.Profile.ExpectedCloseDate,
            "amount" => deal => DealDecimal.ParseScaled(deal.Profile.Amount.Amount),
            "opportunityScore" => deal => DealDecimal.ParseScaled(deal.Profile.OpportunityScore),
            _ => deal => deal.UpdatedAt
        };
        return descending
            ? deals.OrderByDescending(key).ThenByDescending(deal => deal.DealId, StringComparer.Ordinal)
            : deals.OrderBy(key).ThenBy(deal => deal.DealId, StringComparer.Ordinal);
    }

    private static bool InvalidDirection(IDictionary<string, string[]> fields)
    {
        fields["sortDirection"] = ["sortDirection must be asc or desc."];
        return true;
    }

    private static DealStageCategory? ParseStageCategory(string? value, IDictionary<string, string[]> fields) => value switch
    {
        null => null,
        "OPEN" => DealStageCategory.Open,
        "WON" => DealStageCategory.Won,
        "LOST" => DealStageCategory.Lost,
        _ => InvalidStageCategory(fields)
    };

    private static DealStageCategory? InvalidStageCategory(IDictionary<string, string[]> fields)
    {
        fields["stageCategory"] = ["stageCategory must be OPEN, WON, or LOST."];
        return null;
    }
}
