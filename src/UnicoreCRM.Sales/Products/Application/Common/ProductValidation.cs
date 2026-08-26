using System.Text.RegularExpressions;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Application.Common;

internal static partial class ProductValidation
{
    private static readonly HashSet<string> Types =
    [
        "physical_product", "service", "subscription", "package", "implementation",
        "support_sla", "addon", "license", "maintenance"
    ];
    private static readonly HashSet<string> Statuses = ["ACTIVE", "INACTIVE", "DRAFT"];
    private static readonly HashSet<string> TaxModes = ["exclusive", "inclusive", "none"];
    private static readonly HashSet<string> BillingCycles = ["one_time", "monthly", "quarterly", "yearly", "custom"];

    internal static bool IsEntityId(string? value) =>
        value is { Length: >= 1 and <= 128 } && EntityIdRegex().IsMatch(value);

    internal static bool TryProfile(
        CreateProductRequest request,
        out ProductProfile? profile,
        out Dictionary<string, string[]> fields,
        out Dictionary<string, string[]> pricingFields) =>
        TryProfile(
            request.Sku, request.Name, request.Type, request.Status, request.Category, request.Description,
            request.Unit, request.UnitPrice, request.CostPrice, request.TaxRate, request.TaxMode,
            request.BillingCycle, request.IsSubscription, request.IsRenewable, request.WarrantyMonths,
            request.DefaultContractMonths, request.Tags, out profile, out fields, out pricingFields);

    internal static bool TryProfile(
        ReplaceProductRequest request,
        out ProductProfile? profile,
        out Dictionary<string, string[]> fields,
        out Dictionary<string, string[]> pricingFields) =>
        TryProfile(
            request.Sku, request.Name, request.Type, request.Status, request.Category, request.Description,
            request.Unit, request.UnitPrice, request.CostPrice, request.TaxRate, request.TaxMode,
            request.BillingCycle, request.IsSubscription, request.IsRenewable, request.WarrantyMonths,
            request.DefaultContractMonths, request.Tags, out profile, out fields, out pricingFields);

    internal static Dictionary<string, string[]> ValidateEffectiveCurrency(
        ProductProfile profile,
        string baseCurrency)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ValidateEffectiveCurrency(profile.UnitPrice, "unitPrice", baseCurrency, fields);
        if (profile.CostPrice is not null)
            ValidateEffectiveCurrency(profile.CostPrice, "costPrice", baseCurrency, fields);
        return fields;
    }

    internal static string? OptionalText(string? value, string field, int maximum, Dictionary<string, string[]> fields)
    {
        if (value is null)
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            fields[field] = [$"{field} cannot contain more than {maximum} characters."];
        return normalized;
    }

    internal static string? RequiredText(string? value, string field, int maximum, Dictionary<string, string[]> fields)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            fields[field] = [$"{field} is required."];
        else if (normalized.Length > maximum)
            fields[field] = [$"{field} cannot contain more than {maximum} characters."];
        return normalized;
    }

    private static bool TryProfile(
        string? sku,
        string? name,
        string? type,
        string? status,
        string? category,
        string? description,
        string? unit,
        ProductMoney? unitPrice,
        ProductMoney? costPrice,
        string? taxRate,
        string? taxMode,
        string? billingCycle,
        bool? isSubscription,
        bool? isRenewable,
        int? warrantyMonths,
        int? defaultContractMonths,
        IReadOnlyList<string?>? tags,
        out ProductProfile? profile,
        out Dictionary<string, string[]> fields,
        out Dictionary<string, string[]> pricingFields)
    {
        fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        pricingFields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var normalizedSku = RequiredText(sku, "sku", 80, fields);
        var normalizedName = RequiredText(name, "name", 200, fields);
        var normalizedCategory = category is null
            ? null
            : OptionalText(category, "category", 160, fields);
        if (category is null)
            fields["category"] = ["category is required."];
        var normalizedDescription = OptionalText(description, "description", 4000, fields);
        var normalizedUnit = RequiredText(unit, "unit", 80, fields);

        if (type is null || !Types.Contains(type))
            fields["type"] = ["type is not a supported Product type."];
        if (status is null || !Statuses.Contains(status))
            fields["status"] = ["status must be ACTIVE, INACTIVE, or DRAFT."];
        if (taxMode is null || !TaxModes.Contains(taxMode))
            fields["taxMode"] = ["taxMode is not supported."];
        if (billingCycle is null || !BillingCycles.Contains(billingCycle))
            fields["billingCycle"] = ["billingCycle is not supported."];
        if (isSubscription is null)
            fields["isSubscription"] = ["isSubscription is required."];
        if (isRenewable is null)
            fields["isRenewable"] = ["isRenewable is required."];
        if (warrantyMonths < 0)
            fields["warrantyMonths"] = ["warrantyMonths must be non-negative."];
        if (defaultContractMonths < 0)
            fields["defaultContractMonths"] = ["defaultContractMonths must be non-negative."];

        var normalizedTags = new List<string>();
        if (tags is null)
            fields["tags"] = ["tags is required."];
        else if (tags.Count > 100)
            fields["tags"] = ["tags cannot contain more than 100 items."];
        else
        {
            for (var index = 0; index < tags.Count; index++)
            {
                if (tags[index] is null || tags[index]!.Length > 120)
                    fields[$"tags[{index}]"] = ["Each tag must be a string containing at most 120 characters."];
                else
                    normalizedTags.Add(tags[index]!.Trim());
            }
        }

        var normalizedUnitPrice = ValidateMoney(unitPrice, "unitPrice", pricingFields);
        var normalizedCostPrice = costPrice is null
            ? null
            : ValidateMoney(costPrice, "costPrice", pricingFields);
        ProductDecimal? normalizedTaxRate = null;
        if (!ProductDecimal.TryParse(taxRate, out var parsedTaxRate)
            || parsedTaxRate.IsNegative
            || ProductDecimal.Compare(parsedTaxRate, new ProductDecimal(100, 0)) > 0)
        {
            pricingFields["taxRate"] = ["taxRate must be a decimal string between 0 and 100 with at most six fractional digits."];
        }
        else
        {
            normalizedTaxRate = parsedTaxRate;
        }

        if (fields.Count != 0 || pricingFields.Count != 0)
        {
            profile = null;
            return false;
        }

        profile = new ProductProfile(
            normalizedSku!,
            normalizedSku!.ToUpperInvariant(),
            normalizedName!,
            type!,
            status!,
            normalizedCategory!,
            normalizedDescription,
            normalizedUnit!,
            normalizedUnitPrice!,
            normalizedCostPrice,
            normalizedTaxRate!.Value.ToString(),
            taxMode!,
            billingCycle!,
            isSubscription!.Value,
            isRenewable!.Value,
            warrantyMonths,
            defaultContractMonths,
            normalizedTags);
        return true;
    }

    private static ProductMoneyValue? ValidateMoney(
        ProductMoney? money,
        string field,
        Dictionary<string, string[]> fields)
    {
        if (money is null
            || !ProductDecimal.TryParse(money.Amount, out var amount)
            || amount.IsNegative
            || money.Currency is null
            || !CurrencyRegex().IsMatch(money.Currency))
        {
            fields[field] = [$"{field} must be a non-negative decimal-string Money value with an uppercase three-letter currency."];
            return null;
        }

        return new ProductMoneyValue(amount.ToString(), money.Currency);
    }

    private static void ValidateEffectiveCurrency(
        ProductMoneyValue money,
        string field,
        string baseCurrency,
        Dictionary<string, string[]> fields)
    {
        if (!string.Equals(money.Currency, baseCurrency, StringComparison.Ordinal))
            fields[field] = [$"{field} currency must match the effective Workspace base currency {baseCurrency}."];
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdRegex();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyRegex();
}
