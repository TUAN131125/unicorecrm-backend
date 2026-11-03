using System.Globalization;
using System.Reflection;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuotePricingTests
{
    [Fact]
    public void Mixed_tax_modes_line_discount_document_discount_fees_and_tax_reconcile()
    {
        var request = Draft("USD", Line("USD", "100", "2", "10", "EXCLUSIVE", "10"),
            Line("USD", "110", taxMode: "INCLUSIVE", tax: "10"), Line("USD", "50")) with
        {
            Adjustments = [new("discount", "Discount", "DISCOUNT", "PERCENTAGE", "10"),
                new("shipping", "Shipping", "SHIPPING_FEE", "FIXED_AMOUNT", "5"),
                new("tax", "Tax", "TAX", "PERCENTAGE", "10")]
        };
        var price = Calculate(request);
        Assert.Equal(360m, price.Subtotal);
        Assert.Equal(54m, price.Discount);
        Assert.Equal(59.1m, price.Tax);
        Assert.Equal(360.1m, price.Total);
        Assert.Equal("18", price.Lines[0].LineTaxAmount.Amount);
        Assert.Equal("10", price.Lines[1].LineTaxAmount.Amount);
        Assert.Equal("0", price.Lines[2].LineTaxAmount.Amount);
        Assert.Equal("SHIPPING", price.Adjustments[1].Type);
        Assert.Equal(price.Total, price.Lines.Sum(x => Decimal(x.LineTotal))
            - Decimal(price.Adjustments[0].Amount) + Decimal(price.Adjustments[1].Amount) + Decimal(price.Adjustments[2].Amount));
    }

    [Theory]
    [InlineData("VND", "1.5", "2")]
    [InlineData("JPY", "1.5", "2")]
    [InlineData("USD", "1.005", "1.01")]
    [InlineData("EUR", "1.005", "1.01")]
    [InlineData("KWD", "1.0005", "1.001")]
    public void Uses_admitted_currency_precision_and_half_up(string currency, string amount, string expected)
    {
        var price = Calculate(Draft(currency, Line(currency, amount)));
        Assert.Equal(expected, price.Lines[0].LineTotal.Amount);
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), price.Total);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1e3")]
    [InlineData("1,000")]
    [InlineData(" 1")]
    [InlineData("1.1234567")]
    [InlineData("79228162514264337593543950336")]
    public void Invalid_decimal_amounts_are_rejected(string amount)
    {
        var errors = Errors();
        Assert.Null(QuotePricing.Calculate(Draft("USD", Line("USD", amount)), errors));
        Assert.Contains("lineItems[0].unitPrice.amount", errors.Keys);
    }

    [Fact]
    public void Arithmetic_overflow_currency_mismatch_zero_quantity_and_invalid_tax_fail_closed()
    {
        foreach (var request in new[] {
            Draft("USD", Line("USD", "79228162514264337593543950335", "2")),
            Draft("USD", Line("EUR", "100")), Draft("USD", Line("USD", "1", "0")),
            Draft("USD", Line("USD", "1", taxMode: "NONE", tax: "1")),
            Draft("USD", Line("USD", "1", discount: "101")), Draft("GBP", Line("GBP", "1")) })
        {
            var errors = Errors();
            Assert.Null(QuotePricing.Calculate(request, errors));
            Assert.NotEmpty(errors);
        }
    }

    [Fact]
    public void Excessive_document_discount_and_duplicate_adjustment_identifiers_are_rejected()
    {
        var request = Draft("USD", Line("USD", "100"));
        foreach (var adjustments in new IReadOnlyList<QuoteAdjustmentInput?>[] {
            [new("d", "Discount", "VOUCHER", "FIXED_AMOUNT", "101")],
            [new("d", "Discount", "PROMOTION", "FIXED_AMOUNT", "1"), new("d", "Fee", "SERVICE_FEE", "FIXED_AMOUNT", "1")] })
        {
            var errors = Errors();
            Assert.Null(QuotePricing.Calculate(request with { Adjustments = adjustments }, errors));
            Assert.NotEmpty(errors);
        }
    }

    [Theory]
    [InlineData("VND", "101", "34", "67")]
    [InlineData("USD", "100.01", "33.33", "66.68")]
    [InlineData("KWD", "1.001", "0.334", "0.667")]
    public void Payment_preview_allocates_authoritative_total_with_final_remainder(string currency, string amount, string first, string remainder)
    {
        var price = Calculate(Draft(currency, Line(currency, amount)));
        var errors = Errors();
        var input = new QuotePaymentAgreementInput("CUSTOM", currency,
            [PaymentLine(1, new("PERCENTAGE") { Percentage = "33.33" }), PaymentLine(2, new("REMAINDER"))]);
        var result = QuotePricing.Payment(input, price, currency, "quote-test", errors)!;
        Assert.Empty(errors);
        Assert.Equal(first, result.Lines[0].PreviewAmount.Amount);
        Assert.Equal(remainder, result.Lines[1].PreviewAmount.Amount);
        Assert.Equal(price.Total, result.Lines.Sum(x => Decimal(x.PreviewAmount)));
        Assert.Equal("quote-test", result.SourceQuoteId);
        Assert.Null(result.AcceptedAt);
        Assert.Equal(1, result.Version);
        Assert.All(result.Lines, x => Assert.StartsWith("payment_line_", x.Id));
    }

    [Fact]
    public void Payment_rejects_wrong_currency_bad_due_rule_duplicate_methods_and_bad_allocation()
    {
        var price = Calculate(Draft("USD", Line("USD", "100")));
        var valid = PaymentLine(1, new("FIXED") { Amount = new("100", "USD") });
        foreach (var line in new[] {
            valid with { AmountRule = new("FIXED") { Amount = new("100", "EUR") } },
            valid with { AmountRule = new("FIXED") { Amount = new("101", "USD") } },
            valid with { DueRule = new("EVENT_RELATIVE") { Event = "ORDER_CONFIRMED", OffsetDays = 3651, DayBasis = "CALENDAR" } },
            valid with { AllowedMethodCodes = ["BANK", "BANK"] }, valid with { Purpose = "INVALID" },
            valid with { FulfillmentGate = "BEFORE_SHIPMENT" }, valid with { Channel = "INVALID" } })
        {
            var errors = Errors();
            QuotePricing.Payment(new("FULL_PAYMENT", "USD", [line]), price, "USD", "quote-test", errors);
            Assert.NotEmpty(errors);
        }
    }

    [Fact]
    public void Supported_due_rules_are_captured_without_executing_payment_lifecycle()
    {
        var price = Calculate(Draft("USD", Line("USD", "100")));
        foreach (var due in new[] {
            new PaymentDueRuleDocument("EVENT_RELATIVE") { Event = "ACCEPTANCE_CONFIRMED", OffsetDays = -3650, DayBasis = "BUSINESS" },
            new PaymentDueRuleDocument("OPERATIONAL_PRECONDITION") { Operation = "BOOKING", LeadDays = 3650 },
            new PaymentDueRuleDocument("MILESTONE") { MilestoneCode = "signed" },
            new PaymentDueRuleDocument("RECURRING_FINITE") { FirstDueDate = "2026-11-01", Interval = "MONTHLY", Count = 120 } })
        {
            var errors = Errors();
            var result = QuotePricing.Payment(new("FULL_PAYMENT", "USD",
                [PaymentLine(1, new("REMAINDER")) with { DueRule = due, FulfillmentGate = "BEFORE_BOOKING" }]),
                price, "USD", "quote-test", errors)!;
            Assert.Empty(errors);
            Assert.Equal(due, result.Lines[0].DueRule);
            Assert.Equal("100", result.Lines[0].PreviewAmount.Amount);
            Assert.Null(result.AcceptedAt);
        }
    }

    [Fact]
    public void Draft_replacement_increments_version_preserves_identity_and_rejects_immutable_state()
    {
        var content = new QuoteDraftContent("First", "USD", "member", null, "[]", "[]", "[]", 1, 0, 0, 1, null, null, null);
        var now = DateTimeOffset.Parse("2026-10-24T00:00:00Z");
        var quote = new Quote("workspace", "quote-test", "Q-0000000001", "CONTACT", "buyer", "DIRECT_SALE", null, null, null, content, now);
        Assert.Equal(1, quote.ResourceVersion);
        Assert.True(quote.ReplaceDraft(content with { Title = "Second", Total = 2 }, now.AddMinutes(1)));
        Assert.Equal(2, quote.ResourceVersion);
        Assert.Equal("Second", quote.Title);
        Assert.Equal("Q-0000000001", quote.QuoteNumber);
        Assert.Equal("quote-test", quote.RootQuoteId);
        Assert.Equal(1, quote.QuoteRevision);
        Assert.Equal(now, quote.CreatedAt);
        Assert.Equal("USD", quote.GrandTotalCurrency);
        typeof(Quote).GetProperty("Status", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(quote, "SENT");
        Assert.False(quote.ReplaceDraft(content, now.AddMinutes(2)));
        Assert.Equal(2, quote.ResourceVersion);
        Assert.Equal("Second", quote.Title);
        typeof(Quote).GetProperty("Status", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(quote, "DRAFT");
        typeof(Quote).GetProperty("ArchivedAt", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(quote, now);
        Assert.False(quote.ReplaceDraft(content, now.AddMinutes(3)));
        Assert.Equal(2, quote.ResourceVersion);
    }

    private static Dictionary<string, string[]> Errors() => new(StringComparer.Ordinal);
    private static decimal Decimal(QuoteMoney money) => decimal.Parse(money.Amount, CultureInfo.InvariantCulture);
    private static QuoteDraftRequest Draft(string currency, params QuoteDraftLineInput[] lines) => new("Quote", currency, lines);
    private static QuoteDraftLineInput Line(string currency, string amount, string quantity = "1", string discount = "0", string taxMode = "NONE", string tax = "0") =>
        new("Manual", quantity, new(amount, currency), discount, taxMode, TaxRate: tax);
    private static QuotePrice Calculate(QuoteDraftRequest request)
    {
        var errors = Errors(); var result = QuotePricing.Calculate(request, errors);
        Assert.Empty(errors); return Assert.IsType<QuotePrice>(result);
    }
    private static QuotePaymentAgreementLineInput PaymentLine(int sequence, PaymentAmountRuleDocument amount) =>
        new(sequence, "Payment", "OTHER", amount, new("FIXED_DATE") { Date = "2026-11-01" }, ["BANK"], "NONE");
}
