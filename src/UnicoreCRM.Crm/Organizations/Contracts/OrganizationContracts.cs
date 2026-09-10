using System.Text.Json.Serialization;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Organizations.Contracts;

public static class OrganizationCapabilities
{
    public static AccessRequirement Create { get; } = AccessRequirement.ForCanonicalCapability("organizations.create");
    public static AccessRequirement Read { get; } = AccessRequirement.ForCanonicalCapability("organizations.read");
    public static AccessRequirement Update { get; } = AccessRequirement.ForCanonicalCapability("organizations.update");
    public static AccessRequirement Delete { get; } = AccessRequirement.ForCanonicalCapability("organizations.delete");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateOrganizationRequest(string? DisplayName, string? LegalName = null, string? TaxCode = null,
    string? Domain = null, string? Website = null, string? Industry = null, string? SizeBand = null,
    int? EmployeeCount = null, decimal? AnnualRevenue = null, string? Email = null, string? Phone = null,
    string? Address = null, OrganizationPostalAddressDocument? AddressDetails = null, string? Source = null,
    string? RelationshipLevel = null, string? Notes = null, string? Status = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateOrganizationRequest(string? DisplayName = null, string? LegalName = null, string? TaxCode = null,
    string? Domain = null, string? Website = null, string? Industry = null, string? SizeBand = null,
    int? EmployeeCount = null, decimal? AnnualRevenue = null, string? Email = null, string? Phone = null,
    string? Address = null, OrganizationPostalAddressDocument? AddressDetails = null, string? Source = null,
    string? RelationshipLevel = null, string? Notes = null, string? Status = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)] public sealed record ArchiveOrganizationRequest;

public sealed record OrganizationPageInfo(string? NextCursor, bool HasNextPage);
public sealed record OrganizationListResponse(IReadOnlyList<OrganizationDocument> Items, OrganizationPageInfo PageInfo);
public sealed record OrganizationOverviewMetrics(int RepresentativeCount)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? OpenDealsCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? CompletedOrdersCount { get; init; }
}
public sealed record OrganizationOverviewReadModel(OrganizationDocument Organization, IReadOnlyList<string> ContactIds,
    OrganizationOverviewMetrics Metrics, IReadOnlyList<object> LinkedRecords, IReadOnlyList<string> AllowedActions,
    long ProjectionVersion, string GeneratedAt);

public sealed record OrganizationMutationResponse(string CommandId, string CorrelationId, string AggregateId,
    string AggregateType, long Version, string OccurredAt, string Outcome, OrganizationDocument Result,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> EmittedEventIds, IReadOnlyList<string> AuditEvidenceIds);

public sealed record OrganizationPostalAddressDocument(string Line1)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Line2 { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Ward { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? District { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Province { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Country { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PostalCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Formatted { get; init; }
}

public sealed record OrganizationDocument(
    string Id,
    string WorkspaceId,
    string DisplayName,
    string Status,
    long Version,
    string CreatedAt,
    string UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LegalName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TaxCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Domain { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Website { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Industry { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SizeBand { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? EmployeeCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public decimal? AnnualRevenue { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Email { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Phone { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Address { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public OrganizationPostalAddressDocument? AddressDetails { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Source { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OwnerId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PrimaryContactId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? ContactRefs { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RelationshipLevel { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExternalRef { get; init; }
}
