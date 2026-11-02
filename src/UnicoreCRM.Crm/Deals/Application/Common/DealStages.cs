using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.Common;

internal static class DealStages
{
    internal static DealForecastCategory DeriveForecast(DealPipelineStage stage, string opportunityScore)
    {
        var score = DealDecimal.ParseScaled(opportunityScore);
        if (stage.ForecastFloor == DealForecastCategory.Commit || score >= DealDecimal.ParseScaled("80")) return DealForecastCategory.Commit;
        if (stage.ForecastFloor == DealForecastCategory.BestCase || score >= DealDecimal.ParseScaled("50")) return DealForecastCategory.BestCase;
        return DealForecastCategory.Pipeline;
    }
}
