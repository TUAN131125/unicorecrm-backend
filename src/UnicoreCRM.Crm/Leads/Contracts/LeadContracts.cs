using System.Text.Json.Serialization;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Leads.Contracts;

public static class LeadCapabilities
{
    public static AccessRequirement Read { get; } = AccessRequirement.ForCanonicalCapability("leads.read");
    public static AccessRequirement Create { get; } = AccessRequirement.ForCanonicalCapability("leads.create");
    public static AccessRequirement Update { get; } = AccessRequirement.ForCanonicalCapability("leads.update");
    public static AccessRequirement Qualify { get; } = AccessRequirement.ForCanonicalCapability("leads.qualify");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract class LeadProfileRequest
{
    public string? DisplayName { get; init; }
    public string? Salutation { get; init; }
    public string? Title { get; init; }
    public string? Department { get; init; }
    public string? Phone { get; init; }
    public string? WorkPhone { get; init; }
    public string? OtherPhone { get; init; }
    public string? Email { get; init; }
    public string? PersonalEmail { get; init; }
    public string? ZaloId { get; init; }
    public string? Facebook { get; init; }
    public string? PreferredChannel { get; init; }
    public bool? DoNotCall { get; init; }
    public bool? DoNotEmail { get; init; }
    public string? CompanyName { get; init; }
    public string? CompanySize { get; init; }
    public string? Industry { get; init; }
    public string? BusinessType { get; init; }
    public string? Website { get; init; }
    public string? TaxCode { get; init; }
    public string? CompanyAddress { get; init; }
    public string? Country { get; init; }
    public string? Province { get; init; }
    public string? District { get; init; }
    public string? Ward { get; init; }
    public string? ContactAddress { get; init; }
    public string? Source { get; init; }
    public string? CampaignId { get; init; }
    public string? OwnerId { get; init; }
    public string? AssignedTeam { get; init; }
    public string? DecisionRole { get; init; }
    public string? Priority { get; init; }
    public IReadOnlyList<LeadInterestedProductInput>? InterestedProducts { get; init; }
    public Money? EstimatedValue { get; init; }
    public string? BudgetRange { get; init; }
    public string? PurchaseTimeline { get; init; }
    public string? PainPoint { get; init; }
    public string? NextFollowUpAt { get; init; }
    public string? FollowUpNote { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? Description { get; init; }
    public string? InternalNotes { get; init; }
    public IReadOnlyList<LeadCustomFieldValue>? CustomFields { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateLeadRequest : LeadProfileRequest;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReplaceLeadProfileRequest : LeadProfileRequest;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Money(string? Amount, string? Currency);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadInterestedProductInput(
    string? ProductId,
    string? InterestLevel,
    int? EstimatedQuantity = null,
    Money? ExpectedBudget = null,
    string? Note = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadCustomFieldValue(
    string? FieldKey,
    string? ValueType,
    string? StringValue = null,
    string? DecimalValue = null,
    bool? BooleanValue = null,
    IReadOnlyList<string>? StringArrayValue = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdvanceLeadWorkStateRequest(
    string? TargetWorkState,
    LeadVerificationProfileInput? VerificationProfile = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadVerificationProfileInput(
    string? CompanyName = null,
    string? PainPoint = null,
    string? NextFollowUpAt = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DisqualifyLeadRequest(string? Reason, string? Evidence);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReopenDisqualifiedLeadRequest;

/// <summary>
/// The adopted <c>LeadRelationshipRef</c>. Its declared vocabulary is <c>CONTACT | ORGANIZATION</c>,
/// which differs from the platform-wide <c>RelationshipRef</c> vocabulary
/// (<c>CONTACT | ORGANIZATION_ACCOUNT</c>). Only <c>CONTACT</c> is produced today, where the two
/// agree; the divergence is a recorded contract fact, not a defect to normalize here.
/// </summary>
public sealed record LeadRelationshipRefDocument(string Type, string Id);

public sealed record LeadDocument(
    string Id,
    string DisplayName,
    Money EstimatedValue,
    long Version,
    string CreatedAt,
    string UpdatedAt,
    string LeadWorkState,
    string Source,
    int Score,
    string OwnerId,
    IReadOnlyList<LeadInterestedProductReadModel> InterestedProducts,
    string ActivityProjection)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Title { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CompanyName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Email { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Phone { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? QualificationOutcome { get; init; }

    /// <summary>
    /// The adopted <c>LeadDocument.relationshipRef</c>. It is the conversion reference for a
    /// positively qualified Lead; the contract declares no <c>contactId</c> field on the Lead.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LeadRelationshipRefDocument? RelationshipRef { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? NextFollowUpAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Priority { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? Tags { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CompanySize { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Industry { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Salutation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Department { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WorkPhone { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OtherPhone { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PersonalEmail { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ZaloId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Facebook { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PreferredChannel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? DoNotCall { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? DoNotEmail { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BusinessType { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Website { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TaxCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CompanyAddress { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Country { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Province { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? District { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Ward { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContactAddress { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CampaignId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AssignedTeam { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DecisionRole { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? BudgetRange { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PurchaseTimeline { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PainPoint { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FollowUpNote { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Description { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InternalNotes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<LeadCustomFieldValue>? CustomFields { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DisqualifiedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DisqualifiedBy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DisqualificationReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DisqualificationNote { get; init; }
}

public sealed record LeadInterestedProductReadModel(
    string Id,
    string ProductId,
    string ProductNameSnapshot,
    string InterestLevel,
    string CreatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SkuSnapshot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ProductTypeSnapshot { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? EstimatedQuantity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Money? ExpectedBudget { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; init; }
}

public sealed record LeadMutationResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    string OccurredAt,
    string Outcome,
    LeadDocument Result,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,
    IReadOnlyList<string> AuditEvidenceIds);

public sealed record LeadProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Instance = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AggregateId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ExpectedVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? CurrentVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IdempotencyKey = null);
