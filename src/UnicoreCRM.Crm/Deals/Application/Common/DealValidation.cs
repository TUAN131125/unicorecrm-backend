using System.Globalization;
using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.Common;

/// <summary>
/// Field-level validation primitives shared by more than one Deals slice.
/// Request-shaped validation for a single operation belongs to that operation's slice.
/// </summary>
internal static partial class DealValidation
{
    internal static IReadOnlyDictionary<string, string[]> ProgressiveProfileErrors(
        DealProfile profile,
        string stageCode,
        DealForecastCategory? forecastCategory)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (profile.Name.Length == 0)
            fields["name"] = ["name is required for this Deal stage."];
        if (profile.BuyerRef.Id.Length == 0)
            fields["buyerRef"] = ["buyerRef is required for this Deal stage."];
        if (profile.OwnerId.Length == 0)
            fields["ownerId"] = ["ownerId is required for this Deal stage."];
        if (stageCode is "PROPOSAL" or "NEGOTIATION")
        {
            if (DealDecimal.ParseScaled(profile.Amount.Amount) <= 0)
                fields["amount"] = ["amount must be greater than zero for PROPOSAL or NEGOTIATION."];
            if (forecastCategory is null)
                fields["forecastCategory"] = ["forecastCategory is required for PROPOSAL or NEGOTIATION."];
        }
        return fields;
    }

    internal static bool IsEntityId(string? value) => value is not null && EntityIdPattern().IsMatch(value);

    internal static string? RequiredText(
        string? input,
        string field,
        int maximum,
        IDictionary<string, string[]> fields) => Text(input, field, 1, maximum, true, fields);

    internal static string? OptionalText(
        string? input,
        string field,
        int maximum,
        IDictionary<string, string[]> fields) => Text(input, field, 0, maximum, false, fields);

    internal static string? OptionalEntity(
        string? input,
        string field,
        IDictionary<string, string[]> fields) => Entity(input, field, false, fields);

    internal static DateTimeOffset? RequiredUtc(
        string? input,
        string field,
        IDictionary<string, string[]> fields) => Utc(input, field, true, fields);

    internal static DealBuyer? Buyer(DealBuyerReference? input, IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            fields["buyerRef"] = ["buyerRef is required."];
            return null;
        }
        if (input.Type is not ("CONTACT" or "ORGANIZATION_ACCOUNT"))
            fields["buyerRef.type"] = ["buyerRef.type must be CONTACT or ORGANIZATION_ACCOUNT."];
        var id = Entity(input.Id, "buyerRef.id", true, fields);
        return id is null || fields.ContainsKey("buyerRef.type") ? null : new DealBuyer(input.Type!, id);
    }

    internal static DealMoneyValue? Money(DealMoney? input, string field, IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            fields[field] = [$"{field} is required."];
            return null;
        }
        if (input.Amount is null || !DecimalPattern().IsMatch(input.Amount))
            fields[$"{field}.amount"] = ["amount must be a base-10 decimal string with at most 6 fractional digits."];
        if (input.Currency is null || !CurrencyPattern().IsMatch(input.Currency))
            fields[$"{field}.currency"] = ["currency must be a three-letter uppercase code."];
        return fields.ContainsKey($"{field}.amount") || fields.ContainsKey($"{field}.currency")
            ? null
            : new DealMoneyValue(input.Amount!, input.Currency!);
    }

    internal static IReadOnlyList<string> EntityList(
        IReadOnlyList<string>? input,
        string field,
        bool required,
        int maximum,
        IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return [];
        }
        if (input.Count > maximum)
            fields[field] = [$"{field} cannot contain more than {maximum} entries."];
        var values = new List<string>(input.Count);
        for (var index = 0; index < input.Count; index++)
        {
            var value = Entity(input[index], $"{field}[{index}]", true, fields);
            if (value is not null)
                values.Add(value);
        }
        if (values.Count != values.Distinct(StringComparer.Ordinal).Count())
            fields[field] = [$"{field} cannot contain duplicate identifiers."];
        return values;
    }

    internal static void RejectLineItems(IReadOnlyList<DealLineInput>? input, IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            fields["lineItems"] = ["lineItems is required."];
            return;
        }
        if (input.Count > 250)
            fields["lineItems"] = ["lineItems cannot contain more than 250 entries."];
        else if (input.Count != 0)
            fields["lineItems"] = ["lineItems require an admitted Products snapshot and pricing contract and are not available in B06 Deals Core."];
    }

    internal static string? Percentage(
        string? input,
        string field,
        bool required,
        IDictionary<string, string[]> fields)
    {
        if (input is null)
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        if (!PercentagePattern().IsMatch(input))
            fields[field] = [$"{field} must be a decimal percentage from 0 through 100 with at most 6 fractional digits."];
        return input;
    }

    internal static DealForecastCategory? ParseForecastCategory(
        string? input,
        string field,
        IDictionary<string, string[]> fields) =>
        input switch
        {
            null => null,
            "COMMIT" => DealForecastCategory.Commit,
            "BEST_CASE" => DealForecastCategory.BestCase,
            "PIPELINE" => DealForecastCategory.Pipeline,
            _ => InvalidForecast(field, fields)
        };

    internal static DateOnly? BusinessDate(
        string? input,
        string field,
        bool required,
        IDictionary<string, string[]> fields)
    {
        if (string.IsNullOrEmpty(input))
        {
            if (required)
                fields[field] = [$"{field} is required."];
            return null;
        }
        if (!DateOnly.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            fields[field] = [$"{field} must be a business date in yyyy-MM-dd format."];
            return null;
        }
        return value;
    }

    internal static DateTimeOffset? Utc(
        string? input,
        string field,
        bool required,
        IDictionary<string, string[]> fields)
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

    internal static string? Entity(
        string? input,
        string field,
        bool required,
        IDictionary<string, string[]> fields)
    {
        var value = Text(input, field, 1, 128, required, fields);
        if (value is not null && !EntityIdPattern().IsMatch(value))
            fields[field] = [$"{field} is not a valid entity identifier."];
        return value;
    }

    private static DealForecastCategory? InvalidForecast(string field, IDictionary<string, string[]> fields)
    {
        fields[field] = [$"{field} must be COMMIT, BEST_CASE, or PIPELINE."];
        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();

    [GeneratedRegex("^-?(0|[1-9][0-9]*)(\\.[0-9]{1,6})?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalPattern();

    [GeneratedRegex("^(?:(?:0|[1-9][0-9]?)(?:\\.[0-9]{1,6})?|100(?:\\.0{1,6})?)$", RegexOptions.CultureInvariant)]
    private static partial Regex PercentagePattern();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();
}
