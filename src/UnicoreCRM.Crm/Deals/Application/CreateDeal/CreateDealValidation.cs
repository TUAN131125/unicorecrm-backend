using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.CreateDeal;

internal static class CreateDealValidation
{
    internal static bool TryCreate(
        CreateDealRequest request,
        out DealProfile? profile,
        out DealStageDefinition? stage,
        out DealForecastCategory forecastCategory,
        out DateTimeOffset? nextActionAt,
        out string? nextActionSummary,
        out string? nextActionTaskId,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = DealValidation.Text(request.Name, "name", 1, 240, true, fields);
        var buyer = DealValidation.Buyer(request.BuyerRef, fields);
        var stageCode = DealValidation.Text(request.StageCode, "stageCode", 1, 120, true, fields);
        stage = stageCode is not null && DealStages.TryGet(stageCode, out var found) ? found : null;
        var amount = DealValidation.Money(request.Amount, "amount", fields);
        var score = DealValidation.Percentage(request.OpportunityScore, "opportunityScore", true, fields);
        var ownerId = DealValidation.Entity(request.OwnerId, "ownerId", true, fields);
        var closeDate = DealValidation.BusinessDate(request.ExpectedCloseDate, "expectedCloseDate", true, fields);
        var interestedProductIds = DealValidation.EntityList(request.InterestedProductIds, "interestedProductIds", true, 250, fields);
        DealValidation.RejectLineItems(request.LineItems, fields);
        var contactId = DealValidation.Entity(request.ContactId, "contactId", false, fields);
        var sourceLeadId = DealValidation.Entity(request.SourceLeadId, "sourceLeadId", false, fields);
        var notes = DealValidation.Text(request.Notes, "notes", 0, 4000, false, fields);
        forecastCategory = DealValidation.ParseForecastCategory(request.ForecastCategory, "forecastCategory", fields) ?? DealForecastCategory.Pipeline;
        nextActionAt = DealValidation.Utc(request.NextActionAt, "nextActionAt", false, fields);
        nextActionSummary = DealValidation.Text(request.NextActionSummary, "nextActionSummary", 0, 1000, false, fields);
        nextActionTaskId = DealValidation.Entity(request.NextActionTaskId, "nextActionTaskId", false, fields);

        errors = fields;
        if (fields.Count != 0 || stage is null)
        {
            profile = null;
            return fields.Count == 0;
        }

        forecastCategory = request.ForecastCategory is null
            ? DealStages.DeriveForecast(stage.Code, score!)
            : forecastCategory;
        profile = new DealProfile(
            name!, buyer!, amount!, score!, ownerId!, closeDate!.Value,
            contactId, sourceLeadId, interestedProductIds, notes);
        return true;
    }
}
