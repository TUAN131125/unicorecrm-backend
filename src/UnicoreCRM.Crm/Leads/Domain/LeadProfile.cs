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
/// <summary>
/// A Leads-owned historical interested-product snapshot. The Product facts are captured once, at the
/// Product resource version recorded in <paramref name="ProductVersionSnapshot"/>, and are never
/// rehydrated from current Product state: a later rename, re-SKU, retype, archive or restore does not
/// rewrite this record.
///
/// No Product price, tax or billing fact is carried. The Lead contract requires none, and its only
/// money field, <paramref name="ExpectedBudget"/>, is the caller's own expected budget rather than a
/// Product price.
/// </summary>
/// <param name="ProductVersionSnapshot">
/// Owner-local capture provenance. <c>LeadInterestedProductReadModel</c> is
/// <c>additionalProperties: false</c> and declares no version field, so this is persisted but never
/// projected onto the wire.
/// </param>
internal sealed record LeadInterestedProduct(
    string Id,
    string ProductId,
    string ProductNameSnapshot,
    string InterestLevel,
    int? EstimatedQuantity,
    LeadMoney? ExpectedBudget,
    string? Note,
    DateTimeOffset CreatedAt)
{
    public string? SkuSnapshot { get; init; }
    public string? ProductTypeSnapshot { get; init; }
    public long? ProductVersionSnapshot { get; init; }
}

/// <summary>
/// The caller-supplied half of an interested-product entry, after structural validation and before
/// any Products resolution. It is what the command fingerprint covers: stable client intent only,
/// never current catalog state, so a replay after a Product rename still matches its stored key.
/// </summary>
internal sealed record LeadInterestedProductIntent(
    string ProductId,
    string InterestLevel,
    int? EstimatedQuantity,
    LeadMoney? ExpectedBudget,
    string? Note);
internal sealed record LeadCustomField(
    string FieldKey,
    string ValueType,
    string? StringValue,
    string? DecimalValue,
    bool? BooleanValue,
    IReadOnlyList<string>? StringArrayValue);
internal sealed record LeadVerificationProfile(string? CompanyName, string? PainPoint, DateTimeOffset? NextFollowUpAt);
