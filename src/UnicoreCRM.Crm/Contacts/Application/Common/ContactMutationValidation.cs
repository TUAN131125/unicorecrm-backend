using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;

namespace UnicoreCRM.Crm.Contacts.Application.Common;

internal static class ContactMutationValidation
{
    internal static bool TryProfile(
        CreateContactRequest request,
        out string? fullName,
        out string? ownerId,
        out ContactProfile? profile,
        out IReadOnlyDictionary<string, string[]> errors) =>
        TryProfile(request.FullName, request.OwnerId, request.Salutation, request.JobTitle, request.Department,
            request.RoleAtCompany, request.WorkEmail, request.PersonalEmail, request.MobilePhone, request.WorkPhone,
            request.OtherPhone, request.ZaloId, request.Facebook, request.PreferredContactChannel, request.Address,
            request.Source, request.DecisionRole, request.RelationshipLevel, request.PainPoint, request.NeedSummary,
            request.Notes, request.Tags, request.DisplayName, out fullName, out ownerId, out profile, out errors);

    internal static bool TryProfile(
        UpdateContactRequest request,
        out string? fullName,
        out string? ownerId,
        out ContactProfile? profile,
        out IReadOnlyDictionary<string, string[]> errors) =>
        TryProfile(request.FullName, request.OwnerId, request.Salutation, request.JobTitle, request.Department,
            request.RoleAtCompany, request.WorkEmail, request.PersonalEmail, request.MobilePhone, request.WorkPhone,
            request.OtherPhone, request.ZaloId, request.Facebook, request.PreferredContactChannel, request.Address,
            request.Source, request.DecisionRole, request.RelationshipLevel, request.PainPoint, request.NeedSummary,
            request.Notes, request.Tags, request.DisplayName, out fullName, out ownerId, out profile, out errors);

    private static bool TryProfile(
        string? suppliedName, string? suppliedOwnerId, string? salutation, string? jobTitle, string? department,
        string? roleAtCompany, string? workEmail, string? personalEmail, string? mobilePhone, string? workPhone,
        string? otherPhone, string? zaloId, string? facebook, string? preferredContactChannel, string? address,
        string? source, string? decisionRole, string? relationshipLevel, string? painPoint, string? needSummary,
        string? notes, IReadOnlyList<string>? suppliedTags, string? displayName,
        out string? fullName, out string? ownerId, out ContactProfile? profile,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        fullName = Text(suppliedName, "fullName", ContactNameBound.MaxLength, true, fields);
        ownerId = Text(suppliedOwnerId, "ownerId", 128, false, fields);
        var normalizedWorkEmail = Email(workEmail, "workEmail", fields);
        var normalizedPersonalEmail = Email(personalEmail, "personalEmail", fields);
        var tags = suppliedTags?.Select(value => value?.Trim()).Where(value => !string.IsNullOrEmpty(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tags?.Length > 100 || tags?.Any(value => value!.Length > 100) == true)
            fields["tags"] = ["tags may contain at most 100 values of at most 100 characters each."];
        errors = fields;
        if (fields.Count != 0)
        {
            profile = null;
            return false;
        }
        profile = new ContactProfile
        {
            Salutation = Text(salutation, "salutation", 40, false, fields),
            JobTitle = Text(jobTitle, "jobTitle", 200, false, fields),
            Department = Text(department, "department", 200, false, fields),
            RoleAtCompany = Text(roleAtCompany, "roleAtCompany", 200, false, fields),
            WorkEmail = normalizedWorkEmail,
            PersonalEmail = normalizedPersonalEmail,
            MobilePhone = Text(mobilePhone, "mobilePhone", 50, false, fields),
            WorkPhone = Text(workPhone, "workPhone", 50, false, fields),
            OtherPhone = Text(otherPhone, "otherPhone", 50, false, fields),
            ZaloId = Text(zaloId, "zaloId", 128, false, fields),
            Facebook = Text(facebook, "facebook", 500, false, fields),
            PreferredContactChannel = Text(preferredContactChannel, "preferredContactChannel", 40, false, fields),
            Address = Text(address, "address", 1000, false, fields),
            Source = Text(source, "source", 120, false, fields),
            DecisionRole = Text(decisionRole, "decisionRole", 120, false, fields),
            RelationshipLevel = Text(relationshipLevel, "relationshipLevel", 120, false, fields),
            PainPoint = Text(painPoint, "painPoint", 2000, false, fields),
            NeedSummary = Text(needSummary, "needSummary", 2000, false, fields),
            Notes = Text(notes, "notes", 4000, false, fields),
            Tags = tags,
            DisplayName = Text(displayName, "displayName", ContactNameBound.MaxLength, false, fields)
        };
        errors = fields;
        return fields.Count == 0;
    }

    private static string? Email(string? value, string field, IDictionary<string, string[]> errors)
    {
        var result = Text(value, field, 320, false, errors);
        if (result is not null && (!result.Contains('@') || result.StartsWith('@') || result.EndsWith('@')))
            errors[field] = [$"{field} must be a valid email address."];
        return result;
    }

    private static string? Text(string? value, string field, int max, bool required, IDictionary<string, string[]> errors)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (required && normalized is null)
            errors[field] = [$"{field} is required."];
        else if (normalized?.Length > max)
            errors[field] = [$"{field} must contain at most {max} characters."];
        return normalized;
    }
}
