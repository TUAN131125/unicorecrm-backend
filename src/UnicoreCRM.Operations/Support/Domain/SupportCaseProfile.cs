namespace UnicoreCRM.Operations.Support.Domain;

/// <summary>
/// The Support-owned case profile: exactly the fields the admitted create and replace
/// request contracts carry. Every foreign identifier here is an unvalidated scalar reference
/// declared by the caller. Support records it and echoes it back; it never reads or asserts
/// the foreign record, because no admitted CRM, Orders or Products reference contract exists.
/// </summary>
internal sealed record SupportCaseProfile(
    string Title,
    string Description,
    SupportCasePriority Priority,
    SupportCaseCategory Category,
    SupportCaseSource Source,
    SupportCaseChannel? Channel,
    SupportCaseRelationship RelationshipRef,
    string? ContactId,
    string? RelatedOrderId,
    string? RelatedProductId,
    string? RelatedOwnedProductId,
    string? OwnerId,
    DateTimeOffset? NextFollowUpAt,
    DateTimeOffset? FirstResponseDueAt,
    DateTimeOffset? ResolutionDueAt,
    IReadOnlyList<string> Tags);

/// <summary>The caller-declared buyer relationship. <c>Type</c> is CONTACT or ORGANIZATION_ACCOUNT.</summary>
internal sealed record SupportCaseRelationship(string Type, string Id);
