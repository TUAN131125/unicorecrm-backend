using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.ReplaceDealProfile;

internal static class ReplaceDealProfileValidation
{
    internal static bool TryProfile(
        ReplaceDealProfileRequest request,
        string opportunityScore,
        string ownerId,
        DateOnly expectedCloseDate,
        out DealProfile? profile,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = DealValidation.Text(request.Name, "name", 1, 240, true, fields);
        var buyer = DealValidation.Buyer(request.BuyerRef, fields);
        var amount = DealValidation.Money(request.Amount, "amount", fields);
        var interestedProductIds = DealValidation.EntityList(request.InterestedProductIds, "interestedProductIds", true, 250, fields);
        DealValidation.RejectLineItems(request.LineItems, fields);
        var contactId = DealValidation.Entity(request.ContactId, "contactId", false, fields);
        var sourceLeadId = DealValidation.Entity(request.SourceLeadId, "sourceLeadId", false, fields);
        var notes = DealValidation.Text(request.Notes, "notes", 0, 4000, false, fields);
        errors = fields;
        profile = fields.Count == 0
            ? new DealProfile(name!, buyer!, amount!, opportunityScore, ownerId, expectedCloseDate, contactId, sourceLeadId, interestedProductIds, notes)
            : null;
        return fields.Count == 0;
    }
}
