using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.Common;

/// <summary>
/// Field-level validation shared by more than one Leads slice. Request-shaped validation
/// for a single operation belongs to that operation's slice.
/// </summary>
internal static partial class LeadValidation
{
    internal static bool TryProfile(
        LeadProfileRequest request,
        out LeadProfile? profile,
        out IReadOnlyDictionary<string, string[]> errors) =>
        TryProfile(request, out profile, out _, out errors);

    /// <summary>
    /// Validates the request shape. Interested products come back as caller intents rather than
    /// snapshots: resolving them needs the Products owner and, for a replace, the Lead's stored
    /// snapshots, neither of which belongs in structural validation. The returned profile therefore
    /// carries an empty interested-product collection, and the command fills it after its idempotency
    /// replay branch.
    /// </summary>
    internal static bool TryProfile(
        LeadProfileRequest request,
        out LeadProfile? profile,
        out IReadOnlyList<LeadInterestedProductIntent> interestedProductIntents,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var displayName = Text(request.DisplayName, "displayName", 1, 200, true, fields);
        var source = Text(request.Source, "source", 1, 120, true, fields);
        var ownerId = Entity(request.OwnerId, "ownerId", true, fields);
        var estimatedValue = Money(request.EstimatedValue, "estimatedValue", true, fields);
        var nextFollowUpAt = Utc(request.NextFollowUpAt, "nextFollowUpAt", false, fields);
        var email = Email(request.Email, "email", fields);
        var personalEmail = Email(request.PersonalEmail, "personalEmail", fields);
        var preferredChannel = Enum(request.PreferredChannel, "preferredChannel", ["phone", "email", "zalo", "facebook", "other"], fields);
        var priority = Enum(request.Priority, "priority", ["low", "medium", "high"], fields);
        var campaignId = Entity(request.CampaignId, "campaignId", false, fields);
        var tags = Tags(request.Tags, fields);
        var customFields = CustomFields(request.CustomFields, fields);
        var productIntents = InterestedProducts(request.InterestedProducts, fields);
        var salutation = Text(request.Salutation, "salutation", 0, 40, false, fields);
        var title = Text(request.Title, "title", 0, 200, false, fields);
        var department = Text(request.Department, "department", 0, 160, false, fields);
        var phone = Text(request.Phone, "phone", 0, 80, false, fields);
        var workPhone = Text(request.WorkPhone, "workPhone", 0, 80, false, fields);
        var otherPhone = Text(request.OtherPhone, "otherPhone", 0, 80, false, fields);
        var zaloId = Text(request.ZaloId, "zaloId", 0, 160, false, fields);
        var facebook = Text(request.Facebook, "facebook", 0, 500, false, fields);
        var companyName = Text(request.CompanyName, "companyName", 0, 240, false, fields);
        var companySize = Text(request.CompanySize, "companySize", 0, 120, false, fields);
        var industry = Text(request.Industry, "industry", 0, 160, false, fields);
        var businessType = Text(request.BusinessType, "businessType", 0, 160, false, fields);
        var website = Text(request.Website, "website", 0, 500, false, fields);
        var taxCode = Text(request.TaxCode, "taxCode", 0, 120, false, fields);
        var companyAddress = Text(request.CompanyAddress, "companyAddress", 0, 1000, false, fields);
        var country = Text(request.Country, "country", 0, 120, false, fields);
        var province = Text(request.Province, "province", 0, 160, false, fields);
        var district = Text(request.District, "district", 0, 160, false, fields);
        var ward = Text(request.Ward, "ward", 0, 160, false, fields);
        var contactAddress = Text(request.ContactAddress, "contactAddress", 0, 1000, false, fields);
        var assignedTeam = Text(request.AssignedTeam, "assignedTeam", 0, 160, false, fields);
        var decisionRole = Text(request.DecisionRole, "decisionRole", 0, 160, false, fields);
        var budgetRange = Text(request.BudgetRange, "budgetRange", 0, 160, false, fields);
        var purchaseTimeline = Text(request.PurchaseTimeline, "purchaseTimeline", 0, 160, false, fields);
        var painPoint = Text(request.PainPoint, "painPoint", 0, 4000, false, fields);
        var followUpNote = Text(request.FollowUpNote, "followUpNote", 0, 4000, false, fields);
        var description = Text(request.Description, "description", 0, 8000, false, fields);
        var internalNotes = Text(request.InternalNotes, "internalNotes", 0, 8000, false, fields);

        errors = fields;
        if (fields.Count != 0)
        {
            profile = null;
            interestedProductIntents = [];
            return false;
        }

        profile = new LeadProfile(
            displayName!,
            salutation,
            title,
            department,
            phone,
            workPhone,
            otherPhone,
            email,
            personalEmail,
            zaloId,
            facebook,
            preferredChannel,
            request.DoNotCall,
            request.DoNotEmail,
            companyName,
            companySize,
            industry,
            businessType,
            website,
            taxCode,
            companyAddress,
            country,
            province,
            district,
            ward,
            contactAddress,
            source!,
            campaignId,
            ownerId!,
            assignedTeam,
            decisionRole,
            priority,
            // Filled by the command after its replay branch, from preserved snapshots plus a single
            // Products batch resolution of the identifiers the Lead does not already carry.
            [],
            estimatedValue!,
            budgetRange,
            purchaseTimeline,
            painPoint,
            nextFollowUpAt,
            followUpNote,
            tags,
            description,
            internalNotes,
            customFields);
        interestedProductIntents = productIntents;
        return true;
    }

    internal static bool IsEntityId(string? value) => value is not null && EntityIdPattern().IsMatch(value);

    /// <summary>
    /// Structural validation only. Duplicate identifiers are rejected because productId is the entry
    /// identity that decides preserve-versus-recapture on replace, so a duplicate would make that
    /// rule non-deterministic. Product existence and eligibility are Products-owned and are decided
    /// later, by Products.
    /// </summary>
    private static IReadOnlyList<LeadInterestedProductIntent> InterestedProducts(
        IReadOnlyList<LeadInterestedProductInput>? input,
        IDictionary<string, string[]> fields)
    {
        if (input is null || input.Count == 0)
            return [];
        if (input.Count > 500)
        {
            fields["interestedProducts"] = ["interestedProducts cannot contain more than 500 entries."];
            return [];
        }

        var intents = new List<LeadInterestedProductIntent>(input.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < input.Count; index++)
        {
            var entry = input[index];
            var prefix = $"interestedProducts[{index}]";
            var productId = Entity(entry.ProductId, $"{prefix}.productId", true, fields);
            var interestLevel = Enum(entry.InterestLevel, $"{prefix}.interestLevel", ["low", "medium", "high"], fields);
            var note = Text(entry.Note, $"{prefix}.note", 0, 2000, false, fields);
            var expectedBudget = Money(entry.ExpectedBudget, $"{prefix}.expectedBudget", false, fields);

            if (entry.EstimatedQuantity is { } quantity && quantity is < 1 or > 1000000)
                fields[$"{prefix}.estimatedQuantity"] = ["estimatedQuantity must be between 1 and 1000000."];

            if (productId is not null && !seen.Add(productId))
                fields[$"{prefix}.productId"] = ["interestedProducts cannot reference the same productId twice."];

            if (productId is null || interestLevel is null)
                continue;

            intents.Add(new LeadInterestedProductIntent(
                productId,
                interestLevel,
                entry.EstimatedQuantity,
                expectedBudget,
                note));
        }

        return intents;
    }

    private static IReadOnlyList<string> Tags(IReadOnlyList<string>? input, IDictionary<string, string[]> fields)
    {
        if (input is null)
            return [];
        if (input.Count > 200)
            fields["tags"] = ["tags cannot contain more than 200 entries."];
        var values = new List<string>(input.Count);
        for (var index = 0; index < input.Count; index++)
        {
            var value = Text(input[index], $"tags[{index}]", 0, 120, false, fields);
            if (value is not null)
                values.Add(value);
        }
        return values;
    }

    private static IReadOnlyList<LeadCustomField> CustomFields(
        IReadOnlyList<LeadCustomFieldValue>? input,
        IDictionary<string, string[]> fields)
    {
        if (input is null)
            return [];
        if (input.Count > 500)
            fields["customFields"] = ["customFields cannot contain more than 500 entries."];
        var values = new List<LeadCustomField>(input.Count);
        for (var index = 0; index < input.Count; index++)
        {
            var item = input[index];
            var prefix = $"customFields[{index}]";
            if (item is null)
            {
                fields[prefix] = ["A custom-field value is required."];
                continue;
            }
            var key = Text(item.FieldKey, $"{prefix}.fieldKey", 1, 160, true, fields);
            var type = item.ValueType;
            if (type is not ("STRING" or "DECIMAL" or "BOOLEAN" or "STRING_ARRAY"))
                fields[$"{prefix}.valueType"] = ["valueType must be STRING, DECIMAL, BOOLEAN, or STRING_ARRAY."];
            var present = (item.StringValue is not null ? 1 : 0)
                + (item.DecimalValue is not null ? 1 : 0)
                + (item.BooleanValue is not null ? 1 : 0)
                + (item.StringArrayValue is not null ? 1 : 0);
            var matches = type switch
            {
                "STRING" => item.StringValue is not null,
                "DECIMAL" => item.DecimalValue is not null && DecimalPattern().IsMatch(item.DecimalValue),
                "BOOLEAN" => item.BooleanValue is not null,
                "STRING_ARRAY" => item.StringArrayValue is not null && item.StringArrayValue.Count <= 200
                    && item.StringArrayValue.All(value => value is not null && value.Length <= 1000),
                _ => false
            };
            if (present != 1 || !matches)
                fields[prefix] = ["Exactly one value matching valueType must be present."];
            if (item.StringValue?.Length > 4000)
                fields[$"{prefix}.stringValue"] = ["stringValue cannot exceed 4000 characters."];
            if (key is not null && matches && present == 1)
                values.Add(new LeadCustomField(key, type!, item.StringValue, item.DecimalValue, item.BooleanValue, item.StringArrayValue));
        }
        return values;
    }

    private static LeadMoney? Money(Money? input, string field, bool required, IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        if (input.Amount is null || !DecimalPattern().IsMatch(input.Amount))
            fields[$"{field}.amount"] = ["amount must be a base-10 decimal string with at most 6 fractional digits."];
        if (input.Currency is null || !CurrencyPattern().IsMatch(input.Currency))
            fields[$"{field}.currency"] = ["currency must be a three-letter uppercase code."];
        return fields.ContainsKey($"{field}.amount") || fields.ContainsKey($"{field}.currency")
            ? null
            : new LeadMoney(input.Amount!, input.Currency!);
    }

    private static string? Email(string? input, string field, IDictionary<string, string[]> fields)
    {
        var value = Text(input, field, 0, 320, false, fields);
        if (value is null)
            return null;
        try
        {
            var parsed = new MailAddress(value);
            if (!string.Equals(parsed.Address, value, StringComparison.OrdinalIgnoreCase))
                throw new FormatException();
        }
        catch (FormatException)
        {
            fields[field] = [$"{field} must be a valid email address."];
        }
        return value;
    }

    private static string? Enum(string? input, string field, string[] values, IDictionary<string, string[]> fields)
    {
        if (input is null)
            return null;
        if (!values.Contains(input, StringComparer.Ordinal))
            fields[field] = [$"{field} is invalid."];
        return input;
    }

    internal static string? Text(
        string? input,
        string field,
        int minimum,
        int maximum,
        bool required,
        IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        var value = input.Trim();
        if (value.Length < minimum || value.Length > maximum)
            fields[field] = [$"{field} must contain between {minimum} and {maximum} characters."];
        return value.Length == 0 && !required ? null : value;
    }

    private static string? Entity(string? input, string field, bool required, IDictionary<string, string[]> fields)
    {
        var value = Text(input, field, 1, 128, required, fields);
        if (value is not null && !EntityIdPattern().IsMatch(value))
            fields[field] = [$"{field} is not a valid entity identifier."];
        return value;
    }

    internal static DateTimeOffset? Utc(string? input, string field, bool required, IDictionary<string, string[]> fields)
    {
        if (string.IsNullOrEmpty(input))
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        if (!input.EndsWith('Z')
            || !DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
        {
            fields[field] = [$"{field} must be a UTC date-time ending in Z."];
            return null;
        }
        return value.ToUniversalTime();
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();

    [GeneratedRegex("^-?(0|[1-9][0-9]*)(\\.[0-9]{1,6})?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalPattern();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();
}
