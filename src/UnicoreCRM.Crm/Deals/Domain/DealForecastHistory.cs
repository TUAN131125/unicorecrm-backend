namespace UnicoreCRM.Crm.Deals.Domain;

internal sealed record DealForecastHistory(
    string Id,
    DateTimeOffset OccurredAt,
    string Actor,
    DateOnly PreviousExpectedCloseDate,
    DateOnly NextExpectedCloseDate,
    string PreviousProbability,
    string NextProbability,
    DealForecastCategory PreviousCategory,
    DealForecastCategory NextCategory);
