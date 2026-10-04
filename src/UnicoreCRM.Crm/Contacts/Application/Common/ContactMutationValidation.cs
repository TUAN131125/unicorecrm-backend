using System.Collections.Frozen;
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

    internal static bool TryPatch(
        UpdateContactRequest request, IReadOnlySet<string> supplied,
        out ContactPatch? patch, out IReadOnlyDictionary<string, string[]> errors)
    {
        patch = null;
        if (supplied.Count == 0)
        {
            errors = new Dictionary<string, string[]> { ["body"] = ["At least one writable Contact field must be supplied."] };
            return false;
        }
        T? Value<T>(string field, T? value) where T : class => supplied.Contains(field) ? value : null;
        TryProfile(
            Value("fullName", request.FullName), Value("ownerId", request.OwnerId),
            Value("salutation", request.Salutation), Value("jobTitle", request.JobTitle),
            Value("department", request.Department), Value("roleAtCompany", request.RoleAtCompany),
            Value("workEmail", request.WorkEmail), Value("personalEmail", request.PersonalEmail),
            Value("mobilePhone", request.MobilePhone), Value("workPhone", request.WorkPhone),
            Value("otherPhone", request.OtherPhone), Value("zaloId", request.ZaloId),
            Value("facebook", request.Facebook), Value("preferredContactChannel", request.PreferredContactChannel),
            Value("address", request.Address), Value("source", request.Source),
            Value("decisionRole", request.DecisionRole), Value("relationshipLevel", request.RelationshipLevel),
            Value("painPoint", request.PainPoint), Value("needSummary", request.NeedSummary),
            Value("notes", request.Notes), Value("tags", request.Tags),
            Value("displayName", request.DisplayName),
            out var fullName, out var ownerId, out var profile, out errors, supplied.Contains("fullName"));

        if (supplied.Contains("ownerId") && request.OwnerId is not null && ownerId is null)
        {
            errors = new Dictionary<string, string[]>(errors) { ["ownerId"] = ["ownerId must reference an active Workspace member."] };
        }
        if (errors.Count != 0 || profile is null) return false;
        patch = new ContactPatch(supplied.ToFrozenSet(StringComparer.Ordinal), fullName, ownerId, profile);
        return true;
    }

    private static bool TryProfile(
        string? suppliedName, string? suppliedOwnerId, string? salutation, string? jobTitle, string? department,
        string? roleAtCompany, string? workEmail, string? personalEmail, string? mobilePhone, string? workPhone,
        string? otherPhone, string? zaloId, string? facebook, string? preferredContactChannel, string? address,
        string? source, string? decisionRole, string? relationshipLevel, string? painPoint, string? needSummary,
        string? notes, IReadOnlyList<string>? suppliedTags, string? displayName,
        out string? fullName, out string? ownerId, out ContactProfile? profile,
        out IReadOnlyDictionary<string, string[]> errors, bool requireName = true)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        fullName = Text(suppliedName, "fullName", ContactNameBound.MaxLength, requireName, fields);
        ownerId = Text(suppliedOwnerId, "ownerId", 128, false, fields);
        var normalizedWorkEmail = Email(workEmail, "workEmail", fields);
        var normalizedPersonalEmail = Email(personalEmail, "personalEmail", fields);
        var tags = suppliedTags?.Select(value => value?.Trim()).OfType<string>().Where(value => value.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tags?.Length > 100 || tags?.Any(value => value.Length > 100) == true)
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
            JobTitle = Text(jobTitle, "jobTitle", 160, false, fields),
            Department = Text(department, "department", 160, false, fields),
            RoleAtCompany = Text(roleAtCompany, "roleAtCompany", 160, false, fields),
            WorkEmail = normalizedWorkEmail,
            PersonalEmail = normalizedPersonalEmail,
            MobilePhone = Text(mobilePhone, "mobilePhone", 50, false, fields),
            WorkPhone = Text(workPhone, "workPhone", 50, false, fields),
            OtherPhone = Text(otherPhone, "otherPhone", 50, false, fields),
            ZaloId = Text(zaloId, "zaloId", 120, false, fields),
            Facebook = Text(facebook, "facebook", 500, false, fields),
            PreferredContactChannel = Choice(preferredContactChannel, "preferredContactChannel", ["phone", "email", "zalo", "facebook", "sms"], fields),
            Address = Text(address, "address", 700, false, fields),
            Source = Text(source, "source", 160, false, fields),
            DecisionRole = Choice(decisionRole, "decisionRole", ["decision_maker", "influencer", "user", "buyer", "technical", "finance", "other"], fields),
            RelationshipLevel = Choice(relationshipLevel, "relationshipLevel", ["cold", "warm", "good", "strong", "vip"], fields),
            PainPoint = Text(painPoint, "painPoint", 5000, false, fields),
            NeedSummary = Text(needSummary, "needSummary", 5000, false, fields),
            Notes = Text(notes, "notes", 5000, false, fields),
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

    private static string? Choice(string? value, string field, IReadOnlyList<string> allowed, IDictionary<string, string[]> errors)
    {
        var normalized = Text(value, field, 80, false, errors)?.ToLowerInvariant();
        if (normalized is not null && !allowed.Contains(normalized, StringComparer.Ordinal))
            errors[field] = [$"{field} must be one of: {string.Join(", ", allowed)}."];
        return normalized;
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
