using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.Common;

internal static class LeadProjection
{
    internal static LeadDocument Document(Lead lead)
    {
        var profile = lead.Profile;
        return new LeadDocument(
            lead.LeadId,
            profile.DisplayName,
            Money(profile.EstimatedValue),
            lead.Version,
            Utc(lead.CreatedAt),
            Utc(lead.UpdatedAt),
            WorkState(lead.WorkState),
            profile.Source,
            lead.Score,
            profile.OwnerId,
            profile.InterestedProducts.Select(Product).ToArray(),
            "NOT_INCLUDED")
        {
            Title = profile.Title,
            CompanyName = profile.CompanyName,
            Email = profile.Email,
            Phone = profile.Phone,
            QualificationOutcome = Outcome(lead.QualificationOutcome),
            NextFollowUpAt = OptionalUtc(profile.NextFollowUpAt),
            Priority = profile.Priority,
            Tags = profile.Tags.Count == 0 ? null : profile.Tags,
            CompanySize = profile.CompanySize,
            Industry = profile.Industry,
            Salutation = profile.Salutation,
            Department = profile.Department,
            WorkPhone = profile.WorkPhone,
            OtherPhone = profile.OtherPhone,
            PersonalEmail = profile.PersonalEmail,
            ZaloId = profile.ZaloId,
            Facebook = profile.Facebook,
            PreferredChannel = profile.PreferredChannel,
            DoNotCall = profile.DoNotCall,
            DoNotEmail = profile.DoNotEmail,
            BusinessType = profile.BusinessType,
            Website = profile.Website,
            TaxCode = profile.TaxCode,
            CompanyAddress = profile.CompanyAddress,
            Country = profile.Country,
            Province = profile.Province,
            District = profile.District,
            Ward = profile.Ward,
            ContactAddress = profile.ContactAddress,
            CampaignId = profile.CampaignId,
            AssignedTeam = profile.AssignedTeam,
            DecisionRole = profile.DecisionRole,
            BudgetRange = profile.BudgetRange,
            PurchaseTimeline = profile.PurchaseTimeline,
            PainPoint = profile.PainPoint,
            FollowUpNote = profile.FollowUpNote,
            Description = profile.Description,
            InternalNotes = profile.InternalNotes,
            CustomFields = profile.CustomFields.Count == 0
                ? null
                : profile.CustomFields.Select(CustomField).ToArray(),
            DisqualifiedAt = OptionalUtc(lead.DisqualifiedAt),
            DisqualifiedBy = lead.DisqualifiedBy,
            DisqualificationReason = lead.DisqualificationReason,
            DisqualificationNote = lead.DisqualificationEvidence
        };
    }

    internal static string Utc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static string? OptionalUtc(DateTimeOffset? value) => value is null ? null : Utc(value.Value);
    private static Money Money(LeadMoney value) => new(value.Amount, value.Currency);
    private static LeadInterestedProductReadModel Product(LeadInterestedProduct value) => new(
        value.Id,
        value.ProductId,
        value.ProductNameSnapshot,
        value.InterestLevel,
        Utc(value.CreatedAt))
    {
        EstimatedQuantity = value.EstimatedQuantity,
        ExpectedBudget = value.ExpectedBudget is null ? null : Money(value.ExpectedBudget),
        Note = value.Note
    };
    private static LeadCustomFieldValue CustomField(LeadCustomField value) => new(
        value.FieldKey,
        value.ValueType,
        value.StringValue,
        value.DecimalValue,
        value.BooleanValue,
        value.StringArrayValue);
    private static string WorkState(LeadWorkState state) => state switch
    {
        LeadWorkState.Contacting => "CONTACTING",
        LeadWorkState.Verifying => "VERIFYING",
        LeadWorkState.Closed => "CLOSED",
        _ => "NEW"
    };
    private static string? Outcome(LeadQualificationOutcome? outcome) => outcome switch
    {
        LeadQualificationOutcome.Disqualified => "DISQUALIFIED",
        _ => null
    };
}
