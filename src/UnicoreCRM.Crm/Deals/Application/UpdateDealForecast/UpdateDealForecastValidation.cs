using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.UpdateDealForecast;

internal static class UpdateDealForecastValidation
{
    internal static bool TryForecast(
        UpdateDealForecastRequest request,
        Deal deal,
        out DateOnly expectedCloseDate,
        out string opportunityScore,
        out DealForecastCategory forecastCategory,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.ExpectedCloseDate is null && request.OpportunityScore is null && request.ForecastCategory is null)
            fields["body"] = ["At least one forecast field is required."];
        expectedCloseDate = request.ExpectedCloseDate is null
            ? deal.Profile.ExpectedCloseDate
            : DealValidation.BusinessDate(request.ExpectedCloseDate, "expectedCloseDate", true, fields) ?? deal.Profile.ExpectedCloseDate;
        opportunityScore = request.OpportunityScore is null
            ? deal.Profile.OpportunityScore
            : DealValidation.Percentage(request.OpportunityScore, "opportunityScore", true, fields) ?? deal.Profile.OpportunityScore;
        forecastCategory = DealValidation.ParseForecastCategory(request.ForecastCategory, "forecastCategory", fields) ?? deal.ForecastCategory;
        errors = fields;
        return fields.Count == 0;
    }
}
