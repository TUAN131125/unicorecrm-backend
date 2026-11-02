using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed record QuoteApprovalAssessment(string Fingerprint, string PolicyVersion,
    IReadOnlyList<QuoteApprovalReasonReadModel> Reasons)
{
    internal bool Required => Reasons.Count != 0;
    internal static QuoteApprovalAssessment Evaluate(Quote quote, QuoteApprovalPolicy policy)
    {
        var model = QuoteProjection.Document(quote);
        var reasons = new List<QuoteApprovalReasonReadModel>();
        if (policy.AlwaysRequireApproval) reasons.Add(new("MANUAL_REVIEW", "Workspace requires approval"));
        if (model.LineItems.Any(line => Number(line.DiscountRate) > policy.LineDiscountPercent))
            reasons.Add(new("LINE_DISCOUNT_LIMIT", "Line discount exceeds Workspace limit"));
        if (quote.SubtotalAmount == 0 ? quote.DiscountTotalAmount > 0
            : quote.DiscountTotalAmount / quote.SubtotalAmount * 100 > policy.TotalDiscountPercent)
            reasons.Add(new("TOTAL_DISCOUNT_LIMIT", "Total discount exceeds Workspace limit"));
        if (!policy.GrandTotalLimits.TryGetValue(quote.Currency, out var limit))
            reasons.Add(new("MANUAL_REVIEW", "Currency has no Workspace approval limit"));
        else if (quote.GrandTotalAmount > limit)
            reasons.Add(new("GRAND_TOTAL_LIMIT", "Grand total exceeds Workspace limit"));
        if (model.PaymentAgreement is { } payment)
        {
            var ordinary = payment.Kind == "FULL_PAYMENT" && payment.Lines.Count == 1 && payment.Lines[0].AmountRule.Type == "REMAINDER"
                && payment.Lines[0].DueRule is { } due
                && due.Type == "EVENT_RELATIVE" && due.Event is "ORDER_CONFIRMED" or "DELIVERY_CONFIRMED" or "INVOICE_ISSUED"
                && due.DayBasis is null or "CALENDAR" && due.OffsetDays.GetValueOrDefault() >= 0;
            if (!ordinary && policy.AdvancedPaymentRequiresApproval)
                reasons.Add(new("CUSTOM_PAYMENT_TERMS", "Advanced payment terms require approval"));
            if (payment.Lines.Any(line => line.DueRule.OffsetDays > policy.PostpaidDays))
                reasons.Add(new("POSTPAID_DAYS_LIMIT", "Postpaid days exceed Workspace limit"));
        }
        return new(ContentFingerprint(quote), policy.Version, reasons);
    }

    internal static string ContentFingerprint(Quote quote)
    {
        var model = QuoteProjection.Document(quote);
        // Server canonical content: decimal values are serialized as invariant normalized strings.
        // Payment snapshot IDs, preview allocations and approval/delivery metadata are excluded.
        var commercial = new
        {
            quote.WorkspaceId, quote.QuoteId, quote.QuoteNumber, quote.RootQuoteId, quote.QuoteRevision,
            model.BuyerRef, model.SourcePath, model.SourceDealId, model.ContactId, model.SourceLeadId,
            model.Title, model.Currency, model.RecipientEmail, model.ValidUntil, model.Notes,
            Lines = model.LineItems.Select(x => new { x.ProductId, x.ProductNameSnapshot, x.SkuSnapshot,
                x.ProductTypeSnapshot, x.DescriptionSnapshot, x.BillingCycleSnapshot, Quantity = Normalize(x.Quantity),
                UnitPrice = Normalize(x.UnitPrice.Amount), DiscountRate = Normalize(x.DiscountRate),
                x.TaxMode, TaxRate = Normalize(x.TaxRate ?? "0"), Subtotal = Normalize(x.LineSubtotal.Amount),
                Discount = Normalize(x.LineDiscountAmount.Amount), Tax = Normalize(x.LineTaxAmount.Amount), Total = Normalize(x.LineTotal.Amount) }),
            Adjustments = model.Adjustments?.Select(x => new { x.Label, x.Type, x.Calculation, Value = Normalize(x.Value), Amount = Normalize(x.Amount.Amount) }),
            Subtotal = Normalize(model.Subtotal.Amount), Discount = Normalize(model.DiscountTotal.Amount),
            Tax = Normalize(model.TaxTotal.Amount), Total = Normalize(model.GrandTotal.Amount),
            Payment = model.PaymentAgreement is not { } p ? null : new { p.Kind, p.Currency, p.PolicyVersion,
                Lines = p.Lines.Select(x => new { x.Sequence, x.Label, x.Purpose, x.AmountRule, x.DueRule,
                    x.AllowedMethodCodes, x.FulfillmentGate, x.PreferredMethodCode, x.Channel, x.InvoicePolicyCode }) },
            quote.SenderName, quote.SenderAddress, quote.SenderEmail, quote.SenderTaxId
        };
        return Hash(JsonSerializer.Serialize(commercial, QuotePricing.Json));
    }
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    private static string Normalize(string value) => Number(value).ToString("G29", CultureInfo.InvariantCulture);
}
