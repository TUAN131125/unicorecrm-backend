namespace UnicoreCRM.Crm.Leads.Domain;

internal sealed record LeadProfile(
    string DisplayName,
    string? Salutation,
    string? Title,
    string? Department,
    string? Phone,
    string? WorkPhone,
    string? OtherPhone,
    string? Email,
    string? PersonalEmail,
    string? ZaloId,
    string? Facebook,
    string? PreferredChannel,
    bool? DoNotCall,
    bool? DoNotEmail,
    string? CompanyName,
    string? CompanySize,
    string? Industry,
    string? BusinessType,
    string? Website,
    string? TaxCode,
    string? CompanyAddress,
    string? Country,
    string? Province,
    string? District,
    string? Ward,
    string? ContactAddress,
    string Source,
    string? CampaignId,
    string OwnerId,
    string? AssignedTeam,
    string? DecisionRole,
    string? Priority,
    IReadOnlyList<LeadInterestedProduct> InterestedProducts,
    LeadMoney EstimatedValue,
    string? BudgetRange,
    string? PurchaseTimeline,
    string? PainPoint,
    DateTimeOffset? NextFollowUpAt,
    string? FollowUpNote,
    IReadOnlyList<string> Tags,
    string? Description,
    string? InternalNotes,
    IReadOnlyList<LeadCustomField> CustomFields)
{
    internal bool HasProgressiveProfile() =>
        DisplayName.Length != 0
        && OwnerId.Length != 0
        && new[] { Phone, WorkPhone, OtherPhone, Email, PersonalEmail, ZaloId, Facebook }
            .Any(value => !string.IsNullOrWhiteSpace(value));

    internal LeadProfile WithVerification(LeadVerificationProfile verification) => this with
    {
        CompanyName = verification.CompanyName ?? CompanyName,
        PainPoint = verification.PainPoint ?? PainPoint,
        NextFollowUpAt = verification.NextFollowUpAt ?? NextFollowUpAt
    };
}

internal sealed record LeadMoney(string Amount, string Currency);
internal sealed record LeadInterestedProduct(
    string Id,
    string ProductId,
    string ProductNameSnapshot,
    string InterestLevel,
    int? EstimatedQuantity,
    LeadMoney? ExpectedBudget,
    string? Note,
    DateTimeOffset CreatedAt);
internal sealed record LeadCustomField(
    string FieldKey,
    string ValueType,
    string? StringValue,
    string? DecimalValue,
    bool? BooleanValue,
    IReadOnlyList<string>? StringArrayValue);
internal sealed record LeadVerificationProfile(string? CompanyName, string? PainPoint, DateTimeOffset? NextFollowUpAt);
