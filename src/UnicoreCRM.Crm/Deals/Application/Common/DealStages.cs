using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.Common;

internal static class DealStages
{
    private static readonly IReadOnlyDictionary<string, DealStageDefinition> Definitions =
        new Dictionary<string, DealStageDefinition>(StringComparer.Ordinal)
        {
            ["DISCOVERY"] = new("DISCOVERY", DealStageCategory.Open, 10),
            ["QUALIFIED"] = new("QUALIFIED", DealStageCategory.Open, 30),
            ["SOLUTION"] = new("SOLUTION", DealStageCategory.Open, 50),
            ["PROPOSAL"] = new("PROPOSAL", DealStageCategory.Open, 65),
            ["NEGOTIATION"] = new("NEGOTIATION", DealStageCategory.Open, 80),
            ["WON"] = new("WON", DealStageCategory.Won, 100),
            ["LOST"] = new("LOST", DealStageCategory.Lost, 0)
        };

    internal static bool TryGet(string stageCode, out DealStageDefinition? definition) =>
        Definitions.TryGetValue(stageCode, out definition);

    internal static DealForecastCategory DeriveForecast(string stageCode, string opportunityScore)
    {
        var score = DealDecimal.ParseScaled(opportunityScore);
        if (stageCode == "NEGOTIATION" || score >= DealDecimal.ParseScaled("80"))
            return DealForecastCategory.Commit;
        if (stageCode is "SOLUTION" or "PROPOSAL" || score >= DealDecimal.ParseScaled("50"))
            return DealForecastCategory.BestCase;
        return DealForecastCategory.Pipeline;
    }
}

internal sealed record DealStageDefinition(string Code, DealStageCategory Category, int DefaultProbability);
