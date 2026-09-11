using System.Text.Json.Serialization;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Customers.Contracts;

public static class CustomerCapabilities
{
    public static AccessRequirement View { get; } = AccessRequirement.ForCanonicalCapability("customers.view");
    public static AccessRequirement Create { get; } = AccessRequirement.ForCanonicalCapability("customers.onboard_existing");
    public static AccessRequirement Edit { get; } = AccessRequirement.ForCanonicalCapability("customers.edit");
    public static AccessRequirement Archive { get; } = AccessRequirement.ForCanonicalCapability("customers.archive");
}

public sealed record RelationshipRefDocument(string Type, string Id);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCustomerRequest(RelationshipRefDocument? RelationshipRef)
{
    public string? Segment { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? Tier { get; init; }
    public string? ServiceLevel { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateCustomerRequest
{
    public string? Segment { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public string? Tier { get; init; }
    public string? ServiceLevel { get; init; }
    public string? Status { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)] public sealed record ArchiveCustomerRequest;

public sealed record CustomerPageInfo(string? NextCursor, bool HasNextPage);
public sealed record CustomerListResponse(IReadOnlyList<CustomerDocument> Items, CustomerPageInfo PageInfo);

public sealed record CustomerMutationResponse(string CommandId, string CorrelationId, string AggregateId,
    string AggregateType, long Version, string OccurredAt, string Outcome, CustomerDocument Result,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> EmittedEventIds, IReadOnlyList<string> AuditEvidenceIds);

public sealed record Customer360Identity(string DisplayName)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContactId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OrganizationId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PrimaryContactId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Email { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Phone { get; init; }
}

public sealed record CustomerStakeholderContactDocument(string RelationshipId, string ContactId, string DisplayName,
    string Role, string EffectiveFrom)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EffectiveTo { get; init; }
}

public sealed record Customer360ReadModel(CustomerDocument Customer, Customer360Identity Identity,
    IReadOnlyDictionary<string, object> Metrics, IReadOnlyList<object> LinkedRecords,
    IReadOnlyList<CustomerStakeholderContactDocument> StakeholderContacts,
    IReadOnlyList<string> AllowedActions, long ProjectionVersion, string GeneratedAt);

public sealed record CustomerDocument(
    string Id,
    string WorkspaceId,
    string CustomerCode,
    string Type,
    RelationshipRefDocument RelationshipRef,
    string Status,
    string? Health,
    string? FirstPurchaseAt,
    string? LastPurchaseAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? OwnerId,
    long Version,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CalculatedHealth { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ManualHealthOverride { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OnboardingStatus { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OnboardingCompletedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CreatedFromEvidenceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ConversionPolicyVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ConversionCorrelationId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SourceSystem { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExternalCustomerRef { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Tier { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ServiceLevel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? CareCadenceDays { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CareOwnerId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Segment { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? Tags { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? NextCareAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LastCareAt { get; init; }
}
