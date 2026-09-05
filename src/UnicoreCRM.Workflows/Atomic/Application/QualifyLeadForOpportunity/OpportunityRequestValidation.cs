using System.Globalization;
using System.Text.RegularExpressions;
using UnicoreCRM.Workflows.Atomic.Application.QualifyLeadForNurture;
using UnicoreCRM.Workflows.Atomic.Contracts;

namespace UnicoreCRM.Workflows.Atomic.Application.QualifyLeadForOpportunity;

internal static partial class OpportunityRequestValidation
{
    internal static IReadOnlyDictionary<string, string[]> Validate(LeadOpportunityQualificationCommand command)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        NurtureRequestValidation.ValidateRelationship(command.Contact, fields);
        Text(command.Name, "deal.name", 1, 320, true, fields);
        Entity(command.OwnerId, "deal.ownerId", true, fields);
        if (command.InterestedProductIds.Count > 250)
            fields["deal.interestedProductIds"] = ["interestedProductIds cannot contain more than 250 entries."];
        for (var index = 0; index < command.InterestedProductIds.Count; index++)
            Entity(command.InterestedProductIds[index], $"deal.interestedProductIds[{index}]", true, fields);
        if (command.InterestedProductIds.Count != command.InterestedProductIds.Distinct(StringComparer.Ordinal).Count())
            fields["deal.interestedProductIds"] = ["interestedProductIds cannot contain duplicates."];
        Text(command.NeedSummary, "deal.needSummary", 0, 4000, false, fields);
        if (string.IsNullOrWhiteSpace(command.NeedSummary) && command.InterestedProductIds.Count == 0)
            fields["deal"] = ["needSummary or at least one interestedProductId is required."];
        BusinessDate(command.ExpectedCloseDate, "deal.expectedCloseDate", fields);
        Money(command.EstimatedValue, fields);
        Text(command.DecisionProcess, "deal.decisionProcess", 0, 2000, false, fields);
        Text(command.BuyingWindow, "deal.buyingWindow", 0, 1000, false, fields);
        if (command.FollowUpTask is { } task)
        {
            Text(task.Title, "deal.followUpTask.title", 1, 320, true, fields);
            Utc(task.DueAt, "deal.followUpTask.dueAt", fields);
            Text(task.Description, "deal.followUpTask.description", 0, 4000, false, fields);
        }
        return fields;
    }

    private static void Money(LeadQualificationMoneyInput? input, IDictionary<string, string[]> fields)
    {
        if (input is null)
            return;
        if (input.Amount is null || !DecimalPattern().IsMatch(input.Amount))
            fields["deal.estimatedValue.amount"] = ["amount must be a base-10 decimal string with at most 6 fractional digits."];
        if (input.Currency is null || !CurrencyPattern().IsMatch(input.Currency))
            fields["deal.estimatedValue.currency"] = ["currency must be a three-letter uppercase code."];
    }

    private static void BusinessDate(string? input, string field, IDictionary<string, string[]> fields)
    {
        if (input is not null
            && !DateOnly.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            fields[field] = [$"{field} must be a business date in yyyy-MM-dd format."];
    }

    private static void Utc(string? input, string field, IDictionary<string, string[]> fields)
    {
        if (string.IsNullOrEmpty(input)
            || !input.EndsWith('Z')
            || !DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            fields[field] = [$"{field} must be a UTC date-time ending in Z."];
    }

    private static void Entity(string? input, string field, bool required, IDictionary<string, string[]> fields)
    {
        var value = Text(input, field, required ? 1 : 0, 128, required, fields);
        if (value is not null && !fields.ContainsKey(field) && !EntityIdPattern().IsMatch(value))
            fields[field] = [$"{field} is not a valid entity identifier."];
    }

    private static string? Text(
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
        if (value.Length == 0 && !required)
            return null;
        if (value.Length < minimum || value.Length > maximum)
            fields[field] = [$"{field} must contain between {minimum} and {maximum} characters."];
        return value;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();

    [GeneratedRegex("^-?(0|[1-9][0-9]*)(\\.[0-9]{1,6})?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalPattern();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();
}
