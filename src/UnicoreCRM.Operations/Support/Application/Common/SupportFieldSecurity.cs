using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Operations.Support.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Operations.Support.Application.Common;

/// <summary>
/// Support-side enforcement of the AccessControl field-security decision.
///
/// <para>Support decides nothing here. AccessControl has already reduced the policy to a per-field
/// <see cref="RecordFieldEnforcement"/>; this type only applies it to Support's own wire vocabulary,
/// which is the one thing AccessControl cannot know. Field access was previously reported to the
/// consumer and enforced nowhere, so a withheld field still travelled in the response and only the
/// browser declined to draw it.</para>
///
/// <para>Frozen representation rules, which invent nothing:</para>
/// <list type="bullet">
/// <item><b>Withheld on an optional wire field</b> - the property is omitted from the response, which
/// is the representation the contract already defines for an absent optional value.</item>
/// <item><b>Withheld on a required wire field</b> - refused. There is no admitted absent or masked
/// representation for a required field, so returning the value anyway would break the policy and
/// inventing a placeholder would break the contract. The operation fails closed instead.</item>
/// <item><b>MASKED</b> - enforced identically to HIDDEN. No masking representation is admitted by any
/// current authority, so Support withholds the value rather than inventing one. The evaluation
/// projection still reports MASKED, so a consumer can label the field correctly.</item>
/// <item><b>READ_ONLY</b> - the value is returned and any command that would change it is refused.</item>
/// </list>
/// </summary>
internal static class SupportFieldSecurity
{
    /// <summary>
    /// The field keys Support can enforce a policy on, mapped to whether the wire contract makes the
    /// field required. Support declares this to AccessControl so a policy naming a field Support
    /// does not project, or one Support cannot omit, is refused instead of silently ignored.
    /// </summary>
    internal static IReadOnlyDictionary<string, bool> EnforceableFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["caseNumber"] = true,
            ["title"] = true,
            ["description"] = true,
            ["status"] = true,
            ["priority"] = true,
            ["category"] = true,
            ["source"] = true,
            ["relationshipRef"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["slaStatus"] = true,
            ["resourceVersion"] = true,
            ["channel"] = false,
            ["contactId"] = false,
            ["relatedOrderId"] = false,
            ["relatedProductId"] = false,
            ["relatedOwnedProductId"] = false,
            ["ownerId"] = false,
            ["firstResponseDueAt"] = false,
            ["resolutionDueAt"] = false,
            ["resolvedAt"] = false,
            ["closedAt"] = false,
            ["nextFollowUpAt"] = false,
            ["reopenedAt"] = false,
            ["tags"] = false,
            ["resolutionSummary"] = false
        };

    internal static IReadOnlyList<string> FieldKeys { get; } = EnforceableFields.Keys.Order(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Removes every withheld optional value from the projection. Required fields are never reached:
    /// a restrictive policy on one of them is refused before any record is projected.
    /// </summary>
    internal static SupportCaseReadModel Project(SupportCaseReadModel model, RecordAccessAuthorization access) =>
        model with
        {
            Channel = Keep(access, "channel") ? model.Channel : null,
            ContactId = Keep(access, "contactId") ? model.ContactId : null,
            RelatedOrderId = Keep(access, "relatedOrderId") ? model.RelatedOrderId : null,
            RelatedProductId = Keep(access, "relatedProductId") ? model.RelatedProductId : null,
            RelatedOwnedProductId = Keep(access, "relatedOwnedProductId") ? model.RelatedOwnedProductId : null,
            OwnerId = Keep(access, "ownerId") ? model.OwnerId : null,
            FirstResponseDueAt = Keep(access, "firstResponseDueAt") ? model.FirstResponseDueAt : null,
            ResolutionDueAt = Keep(access, "resolutionDueAt") ? model.ResolutionDueAt : null,
            ResolvedAt = Keep(access, "resolvedAt") ? model.ResolvedAt : null,
            ClosedAt = Keep(access, "closedAt") ? model.ClosedAt : null,
            NextFollowUpAt = Keep(access, "nextFollowUpAt") ? model.NextFollowUpAt : null,
            ReopenedAt = Keep(access, "reopenedAt") ? model.ReopenedAt : null,
            Tags = Keep(access, "tags") ? model.Tags : null,
            ResolutionSummary = Keep(access, "resolutionSummary") ? model.ResolutionSummary : null
        };

    /// <summary>
    /// The refusal a caller receives when a restrictive policy names a field Support cannot withhold.
    /// It is returned before any record is read or projected.
    /// </summary>
    internal static SupportOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0
            ? null
            : new SupportOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                "A field-security policy applies to a field this resource cannot withhold, so the request is refused rather than returning a value the policy forbids.");

    /// <summary>
    /// Refuses a profile replacement that would change a field the caller may not write. The check
    /// compares the requested profile against the stored aggregate, so replacing a field with the
    /// value it already holds is not a write.
    /// </summary>
    internal static SupportOperationError? GuardProfileWrite(
        RecordAccessAuthorization access,
        SupportCase current,
        SupportCaseProfile profile)
    {
        var blocked = new List<string>();
        Check(access, blocked, "title", current.Title, profile.Title);
        Check(access, blocked, "description", current.Description, profile.Description);
        Check(access, blocked, "priority", current.Priority, profile.Priority);
        Check(access, blocked, "category", current.Category, profile.Category);
        Check(access, blocked, "source", current.Source, profile.Source);
        Check(access, blocked, "channel", current.Channel, profile.Channel);
        Check(access, blocked, "relationshipRef", (current.RelationshipType, current.RelationshipId), (profile.RelationshipRef.Type, profile.RelationshipRef.Id));
        Check(access, blocked, "contactId", current.ContactId, profile.ContactId);
        Check(access, blocked, "relatedOrderId", current.RelatedOrderId, profile.RelatedOrderId);
        Check(access, blocked, "relatedProductId", current.RelatedProductId, profile.RelatedProductId);
        Check(access, blocked, "relatedOwnedProductId", current.RelatedOwnedProductId, profile.RelatedOwnedProductId);
        Check(access, blocked, "ownerId", current.OwnerId, profile.OwnerId);
        Check(access, blocked, "nextFollowUpAt", current.NextFollowUpAt, profile.NextFollowUpAt);
        Check(access, blocked, "firstResponseDueAt", current.FirstResponseDueAt, profile.FirstResponseDueAt);
        Check(access, blocked, "resolutionDueAt", current.ResolutionDueAt, profile.ResolutionDueAt);
        if (!access.CanWrite("tags") && !current.Tags.SequenceEqual(profile.Tags, StringComparer.Ordinal))
            blocked.Add("tags");
        return Refusal(blocked);
    }

    /// <summary>
    /// Refuses a creation that populates a field the caller may not write. Creation has no stored
    /// value to compare against, so every field the request actually sets counts as a write.
    /// </summary>
    internal static SupportOperationError? GuardCreateWrite(RecordAccessAuthorization access, SupportCaseProfile profile)
    {
        var written = new List<string> { "title", "description", "priority", "category", "source", "relationshipRef" };
        if (profile.Channel is not null) written.Add("channel");
        if (profile.ContactId is not null) written.Add("contactId");
        if (profile.RelatedOrderId is not null) written.Add("relatedOrderId");
        if (profile.RelatedProductId is not null) written.Add("relatedProductId");
        if (profile.RelatedOwnedProductId is not null) written.Add("relatedOwnedProductId");
        if (profile.OwnerId is not null) written.Add("ownerId");
        if (profile.NextFollowUpAt is not null) written.Add("nextFollowUpAt");
        if (profile.FirstResponseDueAt is not null) written.Add("firstResponseDueAt");
        if (profile.ResolutionDueAt is not null) written.Add("resolutionDueAt");
        if (profile.Tags.Count != 0) written.Add("tags");
        return Refusal(written.Where(fieldKey => !access.CanWrite(fieldKey)).ToList());
    }

    internal static SupportOperationError? GuardFieldWrite(RecordAccessAuthorization access, params string[] fieldKeys)
    {
        var blocked = fieldKeys.Where(fieldKey => !access.CanWrite(fieldKey)).ToList();
        return Refusal(blocked);
    }

    private static bool Keep(RecordAccessAuthorization access, string fieldKey) => access.CanRead(fieldKey);

    private static void Check<T>(
        RecordAccessAuthorization access,
        List<string> blocked,
        string fieldKey,
        T current,
        T requested)
    {
        if (!access.CanWrite(fieldKey) && !EqualityComparer<T>.Default.Equals(current, requested))
            blocked.Add(fieldKey);
    }

    private static SupportOperationError? Refusal(List<string> blocked) =>
        blocked.Count == 0
            ? null
            : new SupportOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                $"Field security does not permit writing: {string.Join(", ", blocked.Order(StringComparer.Ordinal))}.");
}
