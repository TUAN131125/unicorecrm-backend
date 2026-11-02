using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnicoreCRM.Sales.Quotes.Contracts;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed record QuotePrice(IReadOnlyList<QuoteLineReadModel> Lines,
    IReadOnlyList<CommercialAdjustmentReadModel> Adjustments, decimal Subtotal, decimal Discount, decimal Tax, decimal Total);

internal static partial class QuotePricing
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static int? Precision(string? currency) => currency switch { "VND" or "JPY" => 0, "USD" or "EUR" => 2, "KWD" => 3, _ => null };
    internal static decimal Round(decimal value, int precision) => decimal.Round(value, precision, MidpointRounding.AwayFromZero);
    internal static string Format(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    internal static QuoteMoney Money(decimal value, string currency) => new(Format(value), currency);
    internal static decimal Number(string? value, string field, IDictionary<string, string[]> errors, decimal max = decimal.MaxValue, bool positive = false)
    {
        if (value is null || value.Length > 40 || !DecimalPattern().IsMatch(value)
            || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result)
            || result < 0 || result > max || positive && result == 0)
        { errors[field] = ["A valid nonnegative decimal string with at most six fractional digits is required."]; return 0; }
        return result;
    }

    internal static QuotePrice? Calculate(QuoteDraftRequest request, IDictionary<string, string[]> errors,
        IReadOnlyList<QuoteLineReadModel>? previous = null)
    {
        var currency = request.Currency ?? "";
        var precision = Precision(currency);
        if (precision is null) errors["currency"] = ["Currency has no admitted Quote precision policy."];
        if (request.LineItems is not { Count: >= 1 and <= 500 }) errors["lineItems"] = ["Provide between one and 500 lines."];
        if (request.Adjustments is { Count: > 100 }) errors["adjustments"] = ["At most 100 adjustments are allowed."];
        if (errors.Count != 0) return null;
        var scale = precision!.Value;
        var lines = new List<QuoteLineReadModel>();
        try
        {
            for (var index = 0; index < request.LineItems!.Count; index++)
            {
                var line = request.LineItems[index]; var path = $"lineItems[{index}]";
                if (line is null) { errors[path] = ["A line is required."]; continue; }
                Text(line.ProductNameSnapshot, path + ".productNameSnapshot", 300, true, errors);
                Text(line.SkuSnapshot, path + ".skuSnapshot", 120, false, errors);
                // The adopted read snapshot is capped at 2000, although the request schema permits
                // 4000. Refuse longer content rather than truncate it or emit an invalid response.
                Text(line.DescriptionSnapshot, path + ".descriptionSnapshot", 2000, false, errors);
                Text(line.ProductTypeSnapshot, path + ".productTypeSnapshot", 120, false, errors);
                Text(line.BillingCycleSnapshot, path + ".billingCycleSnapshot", 120, false, errors);
                if (line.ProductId is not null && !EntityId(line.ProductId)) errors[path + ".productId"] = ["Invalid Product identifier."];
                var quantity = Number(line.Quantity, path + ".quantity", errors, positive: true);
                var price = Number(line.UnitPrice?.Amount, path + ".unitPrice.amount", errors);
                if (line.UnitPrice?.Currency != currency) errors[path + ".unitPrice.currency"] = ["Line and Quote currency must match; conversion is not implicit."];
                var discount = Number(line.DiscountRate, path + ".discountRate", errors, 100);
                var tax = line.TaxRate is null ? 0 : Number(line.TaxRate, path + ".taxRate", errors, 100);
                if (line.TaxMode is not ("EXCLUSIVE" or "INCLUSIVE" or "NONE")) errors[path + ".taxMode"] = ["Invalid tax mode."];
                if (line.TaxMode == "NONE" && tax != 0) errors[path + ".taxRate"] = ["No-tax lines require a zero tax rate."];
                if (errors.Count != 0) continue;
                var subtotal = Round(checked(quantity * price), scale);
                var lineDiscount = Round(subtotal * discount / 100, scale);
                var basis = subtotal - lineDiscount;
                var lineTax = line.TaxMode switch
                { "EXCLUSIVE" => Round(basis * tax / 100, scale), "INCLUSIVE" => Round(basis - basis / (1 + tax / 100), scale), _ => 0 };
                var total = line.TaxMode == "EXCLUSIVE" ? basis + lineTax : basis;
                // Replacement has no client line identifier. Retain the owner's prior identifier
                // for the same positional Product/manual line; new lines receive server IDs.
                var old = previous is not null && index < previous.Count && previous[index].ProductId == line.ProductId ? previous[index] : null;
                lines.Add(new(old?.Id ?? "quote_line_" + Guid.NewGuid().ToString("N"), line.ProductNameSnapshot!, Format(quantity),
                    Money(price, currency), Format(discount), line.TaxMode!, Money(subtotal, currency), Money(lineDiscount, currency),
                    Money(lineTax, currency), Money(total, currency))
                { ProductId = line.ProductId, SkuSnapshot = line.SkuSnapshot, ProductTypeSnapshot = line.ProductTypeSnapshot,
                    DescriptionSnapshot = line.DescriptionSnapshot, BillingCycleSnapshot = line.BillingCycleSnapshot, TaxRate = Format(tax) });
            }
            if (errors.Count != 0) return null;
            var subtotalTotal = lines.Sum(x => decimal.Parse(x.LineSubtotal.Amount, CultureInfo.InvariantCulture));
            var lineDiscountTotal = lines.Sum(x => decimal.Parse(x.LineDiscountAmount.Amount, CultureInfo.InvariantCulture));
            var lineTaxTotal = lines.Sum(x => decimal.Parse(x.LineTaxAmount.Amount, CultureInfo.InvariantCulture));
            var lineGross = lines.Sum(x => decimal.Parse(x.LineTotal.Amount, CultureInfo.InvariantCulture));
            var net = subtotalTotal - lineDiscountTotal;
            var adjustmentValues = new List<(QuoteAdjustmentInput Input, decimal Value)>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in request.Adjustments ?? [])
            {
                if (item is null) { errors["adjustments"] = ["Adjustment is required."]; continue; }
                if (!EntityId(item.Id) || !ids.Add(item.Id!)) errors["adjustments.id"] = ["Adjustment identifiers must be valid and unique."];
                Text(item.Label, "adjustments.label", 200, true, errors);
                if (item.Type is not ("DISCOUNT" or "VOUCHER" or "PROMOTION" or "TAX" or "SHIPPING_FEE" or "SERVICE_FEE" or "INSTALLATION_FEE" or "CONSULTATION_FEE" or "SURCHARGE")) errors["adjustments.type"] = ["Invalid adjustment type."];
                if (item.Calculation is not ("FIXED_AMOUNT" or "PERCENTAGE")) errors["adjustments.calculation"] = ["Invalid adjustment calculation."];
                adjustmentValues.Add((item, Number(item.Value, "adjustments.value", errors, item.Calculation == "PERCENTAGE" ? 100 : decimal.MaxValue)));
            }
            if (errors.Count != 0) return null;
            decimal Amount((QuoteAdjustmentInput Input, decimal Value) item, decimal basis) =>
                Round(item.Input.Calculation == "PERCENTAGE" ? basis * item.Value / 100 : item.Value, scale);
            bool IsDiscount(string? type) => type is "DISCOUNT" or "VOUCHER" or "PROMOTION";
            var docDiscount = adjustmentValues.Where(x => IsDiscount(x.Input.Type)).Sum(x => Amount(x, net));
            var fees = adjustmentValues.Where(x => !IsDiscount(x.Input.Type) && x.Input.Type != "TAX").Sum(x => Amount(x, net));
            if (docDiscount > net + fees) errors["adjustments"] = ["Document discount exceeds the commercial amount."];
            var taxBasis = Math.Max(0, net + fees - docDiscount);
            var docTax = adjustmentValues.Where(x => x.Input.Type == "TAX").Sum(x => Amount(x, taxBasis));
            var adjustments = adjustmentValues.Select(x => new CommercialAdjustmentReadModel(x.Input.Id!, x.Input.Label!, x.Input.Type == "SHIPPING_FEE" ? "SHIPPING" : x.Input.Type!,
                x.Input.Calculation!, Format(x.Value), Money(Amount(x, x.Input.Type == "TAX" ? taxBasis : net), currency))).ToArray();
            var grand = Round(lineGross + fees - docDiscount + docTax, scale);
            return errors.Count != 0 ? null : new(lines, adjustments, subtotalTotal, lineDiscountTotal + docDiscount, lineTaxTotal + docTax, grand);
        }
        catch (OverflowException) { errors["lineItems"] = ["Commercial amount exceeds supported decimal range."]; return null; }
    }

    internal static PaymentAgreementSnapshotDocument? Payment(QuotePaymentAgreementInput? input, QuotePrice price,
        string currency, string quoteId, IDictionary<string, string[]> errors, PaymentAgreementSnapshotDocument? previous = null)
    {
        if (input is null) return null;
        if (input.Currency != currency
            || input.Kind is not ("FULL_PAYMENT" or "DEPOSIT_AND_BALANCE" or "INSTALLMENT" or "MILESTONE" or "CUSTOM")
            || input.Lines is not { Count: >= 1 and <= 100 })
        { errors["paymentAgreement"] = ["Invalid Draft payment agreement."]; return null; }
        var result = new List<PaymentAgreementLineSnapshotDocument>(); decimal allocated = 0;
        Text(input.PolicyVersion, "paymentAgreement.policyVersion", 160, false, errors);
        var ordered = input.Lines.OrderBy(line => line?.Sequence ?? int.MaxValue).ToArray();
        var sequences = new HashSet<int>();
        try
        {
            for (var i = 0; i < ordered.Length; i++)
            {
                var line = ordered[i];
                if (line is null || line.Sequence is < 1 or > 1000 || !sequences.Add(line.Sequence) || line.AmountRule is null || line.DueRule is null)
                { errors["paymentAgreement.lines"] = ["Payment lines must be valid and ordered."]; continue; }
                Text(line.Label, "paymentAgreement.lines.label", 240, true, errors);
                Text(line.InvoicePolicyCode, "paymentAgreement.lines.invoicePolicyCode", 100, false, errors);
                if (line.Purpose is not ("FULL" or "DEPOSIT" or "BALANCE" or "INSTALLMENT" or "MILESTONE" or "OTHER")) errors["paymentAgreement.lines.purpose"] = ["Invalid payment purpose."];
                if (line.Channel is not null and not ("BANK" or "ONLINE_GATEWAY" or "POS" or "CARRIER" or "OFFLINE" or "EXTERNAL")) errors["paymentAgreement.lines.channel"] = ["Invalid payment channel."];
                if (line.AllowedMethodCodes is not { Count: >= 1 } || line.AllowedMethodCodes.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100)
                    || line.PreferredMethodCode is not null && !line.AllowedMethodCodes.Contains(line.PreferredMethodCode)) errors["paymentAgreement.lines.allowedMethodCodes"] = ["Valid allowed payment methods are required."];
                if (line.AllowedMethodCodes?.Distinct(StringComparer.Ordinal).Count() != line.AllowedMethodCodes?.Count) errors["paymentAgreement.lines.allowedMethodCodes"] = ["Payment method codes must be unique."];
                if (line.FulfillmentGate is not ("NONE" or "BEFORE_COMPLETION" or "BEFORE_BOOKING" or "BEFORE_DISPATCH")) errors["paymentAgreement.lines.fulfillmentGate"] = ["Invalid fulfillment gate."];
                decimal amount;
                switch (line.AmountRule.Type)
                {
                    case "REMAINDER":
                        if (i != ordered.Length - 1) errors["paymentAgreement.lines.amountRule"] = ["Remainder must be the final line."];
                        amount = price.Total - allocated; break;
                    case "PERCENTAGE": amount = Round(price.Total * Number(line.AmountRule.Percentage, "paymentAgreement.lines.percentage", errors, 100) / 100, Precision(currency)!.Value); break;
                    case "FIXED":
                        amount = Number(line.AmountRule.Amount?.Amount, "paymentAgreement.lines.amount", errors);
                        if (line.AmountRule.Amount?.Currency != currency) errors["paymentAgreement.lines.currency"] = ["Payment currency must match Quote."];
                        amount = Round(amount, Precision(currency)!.Value); break;
                    default: errors["paymentAgreement.lines.amountRule"] = ["Invalid payment amount rule."]; amount = 0; break;
                }
                if (amount < 0) errors["paymentAgreement.lines.amountRule"] = ["Payment allocation exceeds total."];
                var due = line.DueRule;
                if (due.Type == "EVENT_RELATIVE")
                {
                    if (due.Event is not ("ORDER_CONFIRMED" or "INVOICE_ISSUED" or "DELIVERY_CONFIRMED" or "ACCEPTANCE_CONFIRMED") || due.OffsetDays is null or < -3650 or > 3650 || due.DayBasis is not ("CALENDAR" or "BUSINESS")) errors["paymentAgreement.lines.dueRule"] = ["Invalid event-relative due rule."];
                }
                else if (due.Type == "FIXED_DATE") { if (!DateOnly.TryParseExact(due.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) errors["paymentAgreement.lines.dueRule"] = ["Invalid due date."]; }
                else if (due.Type == "MILESTONE") { if (string.IsNullOrWhiteSpace(due.MilestoneCode) || due.MilestoneCode.Length > 100) errors["paymentAgreement.lines.dueRule"] = ["A milestone of at most 100 characters is required."]; }
                else if (due.Type == "OPERATIONAL_PRECONDITION") { if (due.Operation is not ("BOOKING" or "DISPATCH" or "COMPLETION") || due.LeadDays is null or < 0 or > 3650) errors["paymentAgreement.lines.dueRule"] = ["Invalid operational due rule."]; }
                else if (due.Type == "RECURRING_FINITE") { if (!DateOnly.TryParseExact(due.FirstDueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || due.Interval is not ("WEEKLY" or "MONTHLY" or "QUARTERLY") || due.Count is null or < 1 or > 120) errors["paymentAgreement.lines.dueRule"] = ["Invalid recurring due rule."]; }
                else errors["paymentAgreement.lines.dueRule"] = ["Due rule is not supported by Draft core."];
                allocated += amount;
                var previousLine = previous?.Lines.FirstOrDefault(x => x.Sequence == line.Sequence && x.Purpose == line.Purpose && x.Label == line.Label);
                result.Add(new(previousLine?.Id ?? "payment_line_" + Guid.NewGuid().ToString("N"), line.Sequence, line.Label, line.Purpose,
                    line.AmountRule, Money(amount, currency), line.DueRule, line.AllowedMethodCodes!, line.FulfillmentGate)
                    { PreferredMethodCode = line.PreferredMethodCode, Channel = line.Channel, InvoicePolicyCode = line.InvoicePolicyCode });
            }
            if (allocated != price.Total) errors["paymentAgreement.lines"] = ["Payment allocations must equal the authoritative Quote total."];
        }
        catch (OverflowException) { errors["paymentAgreement"] = ["Payment allocation exceeds supported decimal range."]; }
        return new(1, input.Kind, currency, result) { SourceQuoteId = quoteId, PolicyVersion = input.PolicyVersion };
    }

    internal static void Text(string? value, string field, int max, bool required, IDictionary<string, string[]> errors)
    { if (required && string.IsNullOrWhiteSpace(value) || value?.Length > max) errors[field] = ["Text is missing or exceeds the allowed length."]; }
    internal static bool EntityId(string? value) => value is not null && value.Length <= 128 && IdPattern().IsMatch(value);
    [GeneratedRegex("^-?(0|[1-9][0-9]*)(\\.[0-9]{1,6})?$", RegexOptions.CultureInvariant)] private static partial Regex DecimalPattern();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)] private static partial Regex IdPattern();
}
