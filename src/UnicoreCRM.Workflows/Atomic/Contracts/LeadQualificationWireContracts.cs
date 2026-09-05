using System.Text.Json.Serialization;

namespace UnicoreCRM.Workflows.Atomic.Contracts;

/// <summary>
/// The adopted <c>QualifyLeadNurtureRequest</c>, unchanged. Unknown members are rejected because the
/// pinned schema is <c>additionalProperties: false</c>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QualifyLeadNurtureRequest(
    LeadQualificationRelationshipRequest? Relationship,
    string? RevisitAt,
    string? Reason,
    string? Note = null,
    /// <summary>
    /// The follow-up Task's owner. The contract places it on the nurture request, not inside the
    /// contact object, so it assigns the Task and never the Contact. Omitted means the Lead owner.
    /// </summary>
    string? OwnerId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QualifyLeadOpportunityRequest(
    LeadQualificationRelationshipRequest? Relationship,
    LeadQualificationOpportunityInput? Deal);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadQualificationOpportunityInput(
    string? Name,
    string? OwnerId,
    IReadOnlyList<string>? InterestedProductIds,
    string? NeedSummary = null,
    string? ExpectedCloseDate = null,
    LeadQualificationMoneyInput? EstimatedValue = null,
    string? DecisionProcess = null,
    string? BuyingWindow = null,
    LeadQualificationFollowUpTaskInput? FollowUpTask = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadQualificationMoneyInput(string? Amount, string? Currency);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadQualificationFollowUpTaskInput(
    string? Title,
    string? DueAt,
    string? Description = null);

/// <summary>
/// The adopted <c>LeadQualificationRelationshipRequest</c>. <c>contact</c> is required by the schema
/// even for EXISTING, where it is ignored for identity and never applied as an update.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadQualificationRelationshipRequest(
    string? Kind,
    string? Mode,
    string? SelectedId = null,
    LeadQualificationContactInput? Contact = null,
    LeadQualificationOrganizationInput? Organization = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadQualificationContactInput(
    string? DisplayName,
    string? Email = null,
    string? Phone = null,
    string? Title = null);

/// <summary>
/// Declared by the pinned schema for the ORGANIZATION_ACCOUNT kind, which this workflow does not
/// admit. It exists so a request carrying it is a clean relationship rejection rather than an
/// unknown-member parse failure.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadQualificationOrganizationInput(
    string? DisplayName,
    string? LegalName = null,
    string? TaxCode = null,
    string? Domain = null,
    string? Phone = null,
    string? Email = null,
    string? Address = null,
    string? Industry = null);

/// <summary>The adopted <c>LeadQualificationWorkflowResponse</c>, unchanged.</summary>
public sealed record LeadQualificationWorkflowResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    string OccurredAt,
    string Outcome,
    LeadQualificationWorkflowResult Result,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,
    IReadOnlyList<string> AuditEvidenceIds);

public sealed record LeadQualificationWorkflowResult(
    string LeadId,
    long LeadVersion,
    string QualificationOutcome,
    LeadQualificationResolvedRelationship Relationship,
    IReadOnlyList<LeadQualificationCreatedResource> CreatedResources)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContactId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TaskId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DealId { get; init; }
}

public sealed record LeadQualificationResolvedRelationship(
    QualificationRelationshipRef RelationshipRef,
    string DisplayName)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContactId { get; init; }
}

/// <summary>
/// The platform-wide <c>RelationshipRef</c> vocabulary (<c>CONTACT | ORGANIZATION_ACCOUNT</c>), which
/// is what the qualification result declares. Only <c>CONTACT</c> is produced.
/// </summary>
public sealed record QualificationRelationshipRef(string Type, string Id);

public sealed record LeadQualificationCreatedResource(
    string ResourceType,
    string ResourceId,
    long ResourceVersion);

public sealed record LeadQualificationProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    string? Detail = null,
    string? Instance = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    string? AggregateId = null,
    long? ExpectedVersion = null,
    long? CurrentVersion = null,
    string? IdempotencyKey = null);
