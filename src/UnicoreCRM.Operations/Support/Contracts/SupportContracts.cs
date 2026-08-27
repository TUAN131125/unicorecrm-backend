using System.Text.Json.Serialization;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Operations.Support.Contracts;

/// <summary>
/// The exact Support capabilities the current operation registry requires. No Support
/// capability is invented: <c>support.complete</c>, <c>support.delete</c> and
/// <c>support.export</c> exist in the canonical capability matrix but no admitted Support
/// operation requires them, so they are not referenced here.
/// </summary>
public static class SupportCapabilities
{
    public static AccessRequirement Read { get; } = AccessRequirement.ForCanonicalCapability("support.read");
    public static AccessRequirement Create { get; } = AccessRequirement.ForCanonicalCapability("support.create");
    public static AccessRequirement Update { get; } = AccessRequirement.ForCanonicalCapability("support.update");
    public static AccessRequirement Assign { get; } = AccessRequirement.ForCanonicalCapability("support.assign");
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SupportBuyerRef(string? Type, string? Id);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateSupportCaseRequest(
    string? Title,
    string? Description,
    string? Priority,
    string? Category,
    string? Source,
    SupportBuyerRef? RelationshipRef,
    string? Channel = null,
    string? ContactId = null,
    string? RelatedOrderId = null,
    string? RelatedProductId = null,
    string? RelatedOwnedProductId = null,
    string? NextFollowUpAt = null,
    IReadOnlyList<string>? Tags = null,
    string? OwnerId = null,
    string? FirstResponseDueAt = null,
    string? ResolutionDueAt = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceSupportCaseProfileRequest(
    string? Title,
    string? Description,
    string? Priority,
    string? Category,
    string? Source,
    SupportBuyerRef? RelationshipRef,
    string? Channel = null,
    string? ContactId = null,
    string? RelatedOrderId = null,
    string? RelatedProductId = null,
    string? RelatedOwnedProductId = null,
    string? OwnerId = null,
    string? NextFollowUpAt = null,
    string? FirstResponseDueAt = null,
    string? ResolutionDueAt = null,
    IReadOnlyList<string>? Tags = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssignSupportCaseRequest(string? OwnerId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TransitionSupportCaseRequest(
    string? NextStatus,
    string? ResolutionSummary = null,
    string? Reason = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddSupportCaseReplyRequest(string? Body);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddSupportCaseInternalNoteRequest(string? Body);

/// <summary>
/// The Support-owned read projection.
///
/// <para><b>Customer enrichment is optional by contract.</b> <c>relationshipRef</c> is the
/// canonical relationship identity and stays required. <c>customerId</c> and
/// <c>customerName</c> are declared optional in <c>SupportCaseReadModel</c>, because a Customer
/// aggregate exists only once effective purchase evidence has been recorded: a Support Case
/// raised against a pre-purchase Contact or Organization Account legitimately has neither.
/// Support emits neither field today, since no admitted request carries them and no admitted
/// Customers reference contract resolves them, and that omission is contract-conformant.</para>
///
/// <para>Mapping <c>customerId</c> to <c>relationshipRef.id</c> is specifically <em>not</em>
/// done. Contact/Organization identity and Customer identity are distinct owners' identities;
/// substituting one for the other, or presenting an identifier as a display name, would
/// fabricate CRM data. See the Support customer identity reconciliation section of
/// CURRENT_IMPLEMENTATION_AUTHORITY.md.</para>
///
/// <para><b>Other fail-closed omissions</b> (all optional in the schema, so these do not affect
/// conformance): <c>contactName</c>, <c>contactEmail</c>, <c>contactPhone</c>,
/// <c>relatedOrderNumber</c>, <c>relatedProductName</c>, <c>ownerName</c>, <c>team</c>,
/// <c>internalSummary</c>, <c>firstRespondedAt</c>, <c>activities</c> and <c>comments</c>. None
/// is Support state. <c>activities</c> and <c>comments</c> in particular require
/// <c>actorName</c>/<c>authorName</c>, which are IdentityAuth-owned profile facts that its only
/// narrow cross-owner contract deliberately withholds; replies and internal notes remain
/// persisted so the projection can be enabled later without a data migration.</para>
///
/// <para>Both gaps are recorded in the Support Core section of
/// CURRENT_IMPLEMENTATION_AUTHORITY.md.</para>
/// </summary>
public sealed record SupportCaseReadModel(
    string Id,
    string CaseNumber,
    string Title,
    string Description,
    string Status,
    string Priority,
    string Category,
    string Source,
    SupportBuyerRef RelationshipRef,
    string CreatedAt,
    string UpdatedAt,
    string SlaStatus,
    long ResourceVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Channel = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ContactId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RelatedOrderId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RelatedProductId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RelatedOwnedProductId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OwnerId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FirstResponseDueAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResolutionDueAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResolvedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ClosedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextFollowUpAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReopenedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Tags = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResolutionSummary = null);

public sealed record SupportCaseMutationResult(SupportCaseReadModel SupportCase);

public sealed record SupportCaseMutationResponse(
    string CommandId,
    string CorrelationId,
    string AggregateId,
    string AggregateType,
    long Version,
    string OccurredAt,
    string Outcome,
    SupportCaseMutationResult Result,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds,
    IReadOnlyList<string> AuditEvidenceIds);

public sealed record SupportCaseListResponse(IReadOnlyList<SupportCaseReadModel> Items, SupportPageInfo PageInfo);

public sealed record SupportPageInfo(
    bool HasNextPage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalCount = null);

public sealed record SupportProblemDetails(
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
