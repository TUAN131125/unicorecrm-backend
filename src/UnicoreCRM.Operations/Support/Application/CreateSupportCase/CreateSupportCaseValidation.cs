using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Operations.Support.Domain;

namespace UnicoreCRM.Operations.Support.Application.CreateSupportCase;

/// <summary>
/// Validates <c>CreateSupportCaseRequest</c> against the verified schema. Creation is
/// restricted to the seven <c>SupportCaseCreateCategory</c> values. Every foreign scalar
/// reference is bound-checked as an identifier only; Support asserts nothing about the
/// foreign record because no admitted reference contract exists.
/// </summary>
internal static class CreateSupportCaseValidation
{
    internal static bool TryProfile(
        CreateSupportCaseRequest request,
        out SupportCaseProfile? profile,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var title = SupportValidation.Text(request.Title, "title", 1, 300, true, fields);
        var description = SupportValidation.Text(request.Description, "description", 1, 10000, true, fields);
        var priority = SupportValidation.Priority(request.Priority, "priority", fields);
        var category = SupportValidation.CreateCategory(request.Category, fields);
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
