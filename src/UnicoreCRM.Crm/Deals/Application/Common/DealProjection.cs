using System.Globalization;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.Common;

internal static class DealProjection
{
    internal static DealReadModel Document(Deal deal)
    {
        var profile = deal.Profile;
        return new DealReadModel(
            deal.DealId,
            profile.Name,
            new DealBuyerReference(profile.BuyerRef.Type, profile.BuyerRef.Id),
            deal.StageCode,
            StageCategory(deal.StageCategory),
            Money(profile.Amount),
            profile.OpportunityScore,
            profile.OwnerId,
            BusinessDate(profile.ExpectedCloseDate),
            profile.InterestedProductIds,
            [],
            deal.Version,
            Utc(deal.CreatedAt),
            Utc(deal.UpdatedAt))
        {
            ContactId = profile.ContactId,
            SourceLeadId = profile.SourceLeadId,
            WonAt = OptionalUtc(deal.WonAt),
            LostAt = OptionalUtc(deal.LostAt),
            ActualCloseDate = deal.ActualCloseDate is null ? null : BusinessDate(deal.ActualCloseDate.Value),
            LostReason = deal.LostReason,
            Notes = profile.Notes,
            ArchivedAt = OptionalUtc(deal.ArchivedAt),
            ArchiveReason = deal.ArchiveReason,
            ForecastCategory = ForecastCategory(deal.ForecastCategory),
            ForecastHistory = deal.ForecastHistory.Count == 0
                ? null
                : deal.ForecastHistory.Select(History).ToArray(),
            StageEnteredAt = Utc(deal.StageEnteredAt),
            NextActionAt = OptionalUtc(deal.NextActionAt),
            NextActionSummary = deal.NextActionSummary,
            NextActionRef = deal.NextActionType is null ? null : new DealNextActionReference(deal.NextActionType, deal.NextActionId),
            WinEvidence = deal.WinEvidenceType is null || deal.WinEvidenceSourceId is null || deal.WinEvidenceOccurredAt is null
                ? null
                : new DealWinEvidence(deal.WinEvidenceType, deal.WinEvidenceSourceId, Utc(deal.WinEvidenceOccurredAt.Value)),
            LostReasonNote = deal.LostReasonNote,
            RecycleDecision = RecycleDecision(deal.RecycleDecision),
            RecycleEligible = deal.RecycleEligible,
            RevisitAt = OptionalUtc(deal.RevisitAt)
        };
    }

    internal static string Utc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    internal static string BusinessDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    internal static DealMoney Money(DealMoneyValue value) => new(value.Amount, value.Currency);
    internal static string ForecastCategory(DealForecastCategory value) => value switch
    {
        DealForecastCategory.Commit => "COMMIT",
        DealForecastCategory.BestCase => "BEST_CASE",
        _ => "PIPELINE"
    };

    private static DealForecastHistoryReadModel History(DealForecastHistory value) => new(
        value.Id,
        Utc(value.OccurredAt),
        BusinessDate(value.PreviousExpectedCloseDate),
        BusinessDate(value.NextExpectedCloseDate),
        value.PreviousProbability,
        value.NextProbability,
        ForecastCategory(value.PreviousCategory),
        ForecastCategory(value.NextCategory),
        value.Actor);

    private static string StageCategory(DealStageCategory value) => value switch
    {
        DealStageCategory.Won => "WON",
        DealStageCategory.Lost => "LOST",
        _ => "OPEN"
    };

    private static string? RecycleDecision(DealRecycleDecision? value) => value switch
    {
        DealRecycleDecision.Recycle => "RECYCLE",
        DealRecycleDecision.Conditional => "CONDITIONAL",
        DealRecycleDecision.DoNotRecycle => "DO_NOT_RECYCLE",
        _ => null
    };

    private static string? OptionalUtc(DateTimeOffset? value) => value is null ? null : Utc(value.Value);
}
