using System.Text.Json.Serialization;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Contacts.Contracts;

public static class ContactCapabilities
{
    public static AccessRequirement Read { get; } = AccessRequirement.ForCanonicalCapability("contacts.read");
    public static AccessRequirement Create { get; } = AccessRequirement.ForCanonicalCapability("contacts.create");
    public static AccessRequirement Update { get; } = AccessRequirement.ForCanonicalCapability("contacts.update");
    // Archive retains the established cross-module capability vocabulary; no hard delete exists.
    public static AccessRequirement Archive { get; } = AccessRequirement.ForCanonicalCapability("contacts.delete");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateContactRequest(
    string? FullName,
    string? OwnerId = null,
    string? Salutation = null,
    string? JobTitle = null,
    string? Department = null,
    string? RoleAtCompany = null,
    string? WorkEmail = null,
    string? PersonalEmail = null,
    string? MobilePhone = null,
    string? WorkPhone = null,
    string? OtherPhone = null,
    string? ZaloId = null,
    string? Facebook = null,
    string? PreferredContactChannel = null,
    string? Address = null,
    string? Source = null,
    string? DecisionRole = null,
    string? RelationshipLevel = null,
    string? PainPoint = null,
    string? NeedSummary = null,
    string? Notes = null,
    IReadOnlyList<string>? Tags = null,
    string? DisplayName = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateContactRequest(
    string? FullName,
    string? OwnerId = null,
    string? Salutation = null,
    string? JobTitle = null,
    string? Department = null,
    string? RoleAtCompany = null,
    string? WorkEmail = null,
    string? PersonalEmail = null,
    string? MobilePhone = null,
    string? WorkPhone = null,
    string? OtherPhone = null,
    string? ZaloId = null,
    string? Facebook = null,
    string? PreferredContactChannel = null,
    string? Address = null,
    string? Source = null,
    string? DecisionRole = null,
    string? RelationshipLevel = null,
    string? PainPoint = null,
    string? NeedSummary = null,
    string? Notes = null,
    IReadOnlyList<string>? Tags = null,
    string? DisplayName = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ArchiveContactRequest;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateContactOrganizationRelationshipRequest(
    string? OrganizationId,
    string? Role,
    bool IsPrimaryAffiliation = false,
    string? EffectiveFrom = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateContactOrganizationRelationshipRequest(string? Role = null, bool? IsPrimaryAffiliation = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateContactCustomerRelationshipRequest(string? CustomerId, string? Role, string? EffectiveFrom = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateContactCustomerRelationshipRequest(string? Role);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EndContactRelationshipRequest(string? EndedReason, string? EffectiveTo = null);

public sealed record PostalAddressDocument(string Line1)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Line2 { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Ward { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? District { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Province { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Country { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PostalCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Formatted { get; init; }
}

public sealed record CommunicationConsentLedgerEntryDocument(
    string Id,
    string Channel,
    string Decision,
    string Source,
    string OccurredAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ActorId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Evidence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExpiresAt { get; init; }
}

public sealed record CommunicationConsentProfileDocument(
    IReadOnlyDictionary<string, string> Current,
    IReadOnlyList<CommunicationConsentLedgerEntryDocument> Ledger,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LawfulBasis { get; init; }
}

public sealed record ContactOrganizationRelationshipDocument(
    string Id,
    string OrganizationAccountId,
    string Role,
    bool IsPrimaryRepresentative,
    string EffectiveFrom,
    string CreatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RoleTitle { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Department { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DecisionRole { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EffectiveTo { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CreatedBy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? UpdatedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? UpdatedBy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EndedReason { get; init; }
}

public sealed record ContactDocument(
    string Id,
    string WorkspaceId,
    string FullName,
    string Status,
    long Version,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ArchivedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Salutation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? JobTitle { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Department { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RoleAtCompany { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WorkEmail { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PersonalEmail { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MobilePhone { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? WorkPhone { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OtherPhone { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ZaloId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Facebook { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PreferredContactChannel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Address { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PostalAddressDocument? AddressDetails { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Source { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OwnerId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CommunicationConsentProfileDocument? Consent { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? DoNotCall { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? DoNotEmail { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? DoNotSms { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? DoNotZalo { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? DoNotContact { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DoNotContactReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DecisionRole { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RelationshipLevel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PainPoint { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? NeedSummary { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? Tags { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<ContactOrganizationRelationshipDocument>? OrganizationRelationships { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DisplayName { get; init; }
}

public sealed record ContactRelationshipTargetDocument(string ModuleKey, string RecordId)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Label { get; init; }
}

public sealed record ContactOrganizationRelationshipSummaryDocument(
    string RelationshipId,
    ContactRelationshipTargetDocument Target,
    string Role,
    bool IsPrimaryAffiliation,
    string Status,
    string EffectiveFrom,
    string CreatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EffectiveTo { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EndedReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CreatedBy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? UpdatedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? UpdatedBy { get; init; }
}

public sealed record ContactCustomerRelationshipSummaryDocument(
    string RelationshipId,
    ContactRelationshipTargetDocument Target,
    string Role,
    string Status,
    string EffectiveFrom,
    string CreatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EffectiveTo { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EndedReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CreatedBy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? UpdatedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? UpdatedBy { get; init; }
}

public sealed record RelationshipLinkedRecordCountsDocument(
    int Tasks = 0, int Activities = 0, int Deals = 0, int Quotes = 0, int Orders = 0,
    int Invoices = 0, int Payments = 0, int Shipping = 0, int Returns = 0, int SupportCases = 0);

public sealed record ContactRelationshipSummaryDocument(
    ContactDocument Contact,
    IReadOnlyList<string> OrganizationIds,
    IReadOnlyList<string> CustomerIds,
    IReadOnlyList<ContactRelationshipTargetDocument> LinkedRecords,
    RelationshipLinkedRecordCountsDocument LinkedRecordCounts,
    IReadOnlyList<string> AllowedActions,
    IReadOnlyList<ContactOrganizationRelationshipSummaryDocument> OrganizationRelationships,
    IReadOnlyList<ContactCustomerRelationshipSummaryDocument> CustomerRelationships,
    long ProjectionVersion,
    string GeneratedAt);

public sealed record ContactMutationResult(ContactDocument Contact)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContactOrganizationRelationshipSummaryDocument? OrganizationRelationship { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContactCustomerRelationshipSummaryDocument? CustomerRelationship { get; init; }
}

public sealed record ContactMutationResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    string OccurredAt,
    string Outcome,
    ContactMutationResult Result,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,
    IReadOnlyList<string> AuditEvidenceIds);
