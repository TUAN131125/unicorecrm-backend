using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Operations.Support.Domain;

namespace UnicoreCRM.Operations.Support.Application.ReplaceSupportCaseProfile;

/// <summary>
/// Validates <c>ReplaceSupportCaseProfileRequest</c>. Unlike creation, replacement accepts
/// the full <c>SupportCaseCategory</c> vocabulary, so a case already carrying one of the five
/// legacy categories can be replaced without losing it. Replacement is total: an omitted
/// optional field clears the stored value.
/// </summary>
internal static class ReplaceSupportCaseProfileValidation
{
    internal static bool TryProfile(
        ReplaceSupportCaseProfileRequest request,
        out SupportCaseProfile? profile,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var title = SupportValidation.Text(request.Title, "title", 1, 300, true, fields);
        var description = SupportValidation.Text(request.Description, "description", 1, 10000, true, fields);
        var priority = SupportValidation.Priority(request.Priority, "priority", fields);
        var category = SupportValidation.Category(request.Category, "category", fields);
        var source = SupportValidation.Source(request.Source, fields);
        var channel = SupportValidation.Channel(request.Channel, fields);
        var relationship = request.RelationshipRef is null
            ? Missing(fields)
            : SupportValidation.Relationship(request.RelationshipRef.Type, request.RelationshipRef.Id, fields);
        var contactId = SupportValidation.Entity(request.ContactId, "contactId", false, fields);
        var relatedOrderId = SupportValidation.Entity(request.RelatedOrderId, "relatedOrderId", false, fields);
        var relatedProductId = SupportValidation.Entity(request.RelatedProductId, "relatedProductId", false, fields);
        var relatedOwnedProductId = SupportValidation.Entity(request.RelatedOwnedProductId, "relatedOwnedProductId", false, fields);
        var ownerId = SupportValidation.Entity(request.OwnerId, "ownerId", false, fields);
        var nextFollowUpAt = SupportValidation.Utc(request.NextFollowUpAt, "nextFollowUpAt", false, fields);
        var firstResponseDueAt = SupportValidation.Utc(request.FirstResponseDueAt, "firstResponseDueAt", false, fields);
        var resolutionDueAt = SupportValidation.Utc(request.ResolutionDueAt, "resolutionDueAt", false, fields);
        var tags = SupportValidation.Tags(request.Tags, fields);

        errors = fields;
        if (fields.Count != 0)
        {
            profile = null;
            return false;
        }

        profile = new SupportCaseProfile(
            title!,
            description!,
            priority!.Value,
            category!.Value,
            source!.Value,
            channel,
            relationship!,
            contactId,
            relatedOrderId,
            relatedProductId,
            relatedOwnedProductId,
            ownerId,
            nextFollowUpAt,
            firstResponseDueAt,
            resolutionDueAt,
            tags);
        return true;
    }

    private static SupportCaseRelationship? Missing(IDictionary<string, string[]> fields)
    {
        fields["relationshipRef"] = ["relationshipRef is required."];
        return null;
    }
}
