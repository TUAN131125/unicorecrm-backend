using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.Common;

internal sealed record ContactAccess(TrustedWorkspaceContext Trusted, RecordAccessAuthorization Authorization);

internal static class ContactFieldSecurity
{
    internal static IReadOnlyDictionary<string, bool> EnforceableFields { get; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = true,
            ["workspaceId"] = true,
            ["fullName"] = true,
            ["status"] = true,
            ["version"] = true,
            ["createdAt"] = true,
            ["updatedAt"] = true,
            ["salutation"] = false,
            ["jobTitle"] = false,
            ["department"] = false,
            ["roleAtCompany"] = false,
            ["workEmail"] = false,
            ["personalEmail"] = false,
            ["mobilePhone"] = false,
            ["workPhone"] = false,
            ["otherPhone"] = false,
            ["zaloId"] = false,
            ["facebook"] = false,
            ["preferredContactChannel"] = false,
            ["address"] = false,
            ["addressDetails"] = false,
            ["source"] = false,
            ["ownerId"] = false,
            ["consent"] = false,
            ["doNotCall"] = false,
            ["doNotEmail"] = false,
            ["doNotSms"] = false,
            ["doNotZalo"] = false,
            ["doNotContact"] = false,
            ["doNotContactReason"] = false,
            ["decisionRole"] = false,
            ["relationshipLevel"] = false,
            ["painPoint"] = false,
            ["needSummary"] = false,
            ["notes"] = false,
            ["tags"] = false,
            ["organizationRelationships"] = false,
            ["displayName"] = false
        };

    internal static IReadOnlyList<string> FieldKeys { get; } =
        EnforceableFields.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static ContactDocument Project(ContactDocument model, RecordAccessAuthorization access) =>
        model with
        {
            Salutation = Keep(access, "salutation", model.Salutation),
            JobTitle = Keep(access, "jobTitle", model.JobTitle),
            Department = Keep(access, "department", model.Department),
            RoleAtCompany = Keep(access, "roleAtCompany", model.RoleAtCompany),
            WorkEmail = Keep(access, "workEmail", model.WorkEmail),
            PersonalEmail = Keep(access, "personalEmail", model.PersonalEmail),
            MobilePhone = Keep(access, "mobilePhone", model.MobilePhone),
            WorkPhone = Keep(access, "workPhone", model.WorkPhone),
            OtherPhone = Keep(access, "otherPhone", model.OtherPhone),
            ZaloId = Keep(access, "zaloId", model.ZaloId),
            Facebook = Keep(access, "facebook", model.Facebook),
            PreferredContactChannel = Keep(access, "preferredContactChannel", model.PreferredContactChannel),
            Address = Keep(access, "address", model.Address),
            AddressDetails = access.CanRead("addressDetails") ? model.AddressDetails : null,
            Source = Keep(access, "source", model.Source),
            OwnerId = Keep(access, "ownerId", model.OwnerId),
            Consent = access.CanRead("consent") ? model.Consent : null,
            DoNotCall = Keep(access, "doNotCall", model.DoNotCall),
            DoNotEmail = Keep(access, "doNotEmail", model.DoNotEmail),
            DoNotSms = Keep(access, "doNotSms", model.DoNotSms),
            DoNotZalo = Keep(access, "doNotZalo", model.DoNotZalo),
            DoNotContact = Keep(access, "doNotContact", model.DoNotContact),
            DoNotContactReason = Keep(access, "doNotContactReason", model.DoNotContactReason),
            DecisionRole = Keep(access, "decisionRole", model.DecisionRole),
            RelationshipLevel = Keep(access, "relationshipLevel", model.RelationshipLevel),
            PainPoint = Keep(access, "painPoint", model.PainPoint),
            NeedSummary = Keep(access, "needSummary", model.NeedSummary),
            Notes = Keep(access, "notes", model.Notes),
            Tags = access.CanRead("tags") ? model.Tags : null,
            OrganizationRelationships = access.CanRead("organizationRelationships") ? model.OrganizationRelationships : null,
            DisplayName = Keep(access, "displayName", model.DisplayName)
        };

    internal static ContactOperationError? UnenforceablePolicy(RecordAccessAuthorization access) =>
        access.UnenforceableFieldKeys.Count == 0
            ? null
            : new ContactOperationError(
                "ACCESS_DENIED",
                403,
                "Access denied",
                "A field-security policy applies to a required Contact field, so the request is refused rather than returning a value the policy forbids.");

    private static string? Keep(RecordAccessAuthorization access, string fieldKey, string? value) =>
        access.CanRead(fieldKey) ? value : null;

    private static bool? Keep(RecordAccessAuthorization access, string fieldKey, bool? value) =>
        access.CanRead(fieldKey) ? value : null;
}

internal sealed class ContactAuthorization(IRecordAccessEvaluator evaluator)
{
    internal const string ResourceKey = "contacts";

    internal async Task<ContactOperationResult<ContactAccess>> AuthorizeAsync(
        ContactRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var authorization = await evaluator.AuthorizeResourceAsync(
            ResourceKey,
            ContactCapabilities.Read.Capability,
            ContactFieldSecurity.FieldKeys,
            RecordAccessRepresentation.Full,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);

        if (authorization.TrustedWorkspace is not { } trusted)
        {
            return ContactOperationResult<ContactAccess>.Failure(
                authorization.Code == "WORKSPACE_MISMATCH"
                    ? ContactErrors.WorkspaceMismatch()
                    : ContactErrors.AccessDenied());
        }
        if (!authorization.IsAllowed)
            return ContactOperationResult<ContactAccess>.Failure(ContactErrors.AccessDenied());

        var unenforceable = ContactFieldSecurity.UnenforceablePolicy(authorization);
        return unenforceable is null
            ? ContactOperationResult<ContactAccess>.Success(new ContactAccess(trusted, authorization))
            : ContactOperationResult<ContactAccess>.Failure(unenforceable);
    }

    internal async Task<ContactOperationError?> EnforceRecordAsync(
        ContactAccess access,
        Contact contact,
        string enforcementPoint,
        ContactRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        var decision = await evaluator.AuthorizeRecordAsync(
            access.Authorization,
            contact.ContactId,
            Facts(contact),
            enforcementPoint,
            new RecordAccessRequestContext(metadata.RequestId, metadata.CorrelationId),
            cancellationToken);
        return decision.IsAllowed ? null : ContactErrors.NotFound();
    }

    internal static RecordAccessFacts Facts(Contact contact) => RecordAccessFacts.Found(contact.OwnerId);
}
