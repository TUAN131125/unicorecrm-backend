using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal static class QuoteRevisionContent
{
    internal static QuoteOperationResult<QuoteDraftContent> Build(Quote source, string quoteId)
    {
        var old = QuoteProjection.Document(source);
        var fields = new Dictionary<string, string[]>();
        var agreement = old.PaymentAgreement;
        var request = new QuoteDraftRequest(old.Title, old.Currency,
            old.LineItems.Select(l => (QuoteDraftLineInput?)new QuoteDraftLineInput(l.ProductNameSnapshot, l.Quantity,
                l.UnitPrice, l.DiscountRate, l.TaxMode, l.ProductId, l.SkuSnapshot, l.ProductTypeSnapshot,
                l.DescriptionSnapshot, l.TaxRate, l.BillingCycleSnapshot)).ToArray(),
            Adjustments: old.Adjustments?.Select(a => (QuoteAdjustmentInput?)new QuoteAdjustmentInput(a.Id, a.Label, a.Type == "SHIPPING" ? "SHIPPING_FEE" : a.Type, a.Calculation, a.Value)).ToArray());
        var price = QuotePricing.Calculate(request, fields);
        if (price is null || fields.Count != 0) return Invalid();
        if (price.Subtotal != source.SubtotalAmount || price.Discount != source.DiscountTotalAmount
            || price.Tax != source.TaxTotalAmount || price.Total != source.GrandTotalAmount
            || price.Lines.Count != old.LineItems.Count) return Invalid();
        for (var i = 0; i < price.Lines.Count; i++)
        {
            var a = price.Lines[i]; var b = old.LineItems[i];
            if (!Same(a.LineSubtotal, b.LineSubtotal) || !Same(a.LineDiscountAmount, b.LineDiscountAmount)
                || !Same(a.LineTaxAmount, b.LineTaxAmount) || !Same(a.LineTotal, b.LineTotal)) return Invalid();
        }
        var input = agreement is null ? null : new QuotePaymentAgreementInput(agreement.Kind, agreement.Currency,
            agreement.Lines.Select(l => new QuotePaymentAgreementLineInput(l.Sequence, l.Label, l.Purpose, l.AmountRule,
                l.DueRule, l.AllowedMethodCodes, l.FulfillmentGate, l.PreferredMethodCode, l.Channel, l.InvoicePolicyCode)).ToArray(), agreement.PolicyVersion);
        var payment = QuotePricing.Payment(input, price, source.Currency, quoteId, fields);
        if (fields.Count != 0) return Invalid();
        return QuoteOperationResult<QuoteDraftContent>.Success(new(source.Title, source.Currency, source.OwnerId!, source.RecipientEmail,
            JsonSerializer.Serialize(price.Lines, QuotePricing.Json), JsonSerializer.Serialize(price.Adjustments, QuotePricing.Json),
            source.ProductSnapshotsJson ?? "[]", price.Subtotal, price.Discount, price.Tax, price.Total, source.ValidUntil,
            source.Notes, payment is null ? null : JsonSerializer.Serialize(payment, QuotePricing.Json)));
    }
    private static bool Same(QuoteMoney a, QuoteMoney b) => a.Currency == b.Currency
        && decimal.TryParse(a.Amount, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var x)
        && decimal.TryParse(b.Amount, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var y) && x == y;
    private static QuoteOperationResult<QuoteDraftContent> Invalid() => QuoteOperationResult<QuoteDraftContent>.Failure(
        new("QUOTE_REVISION_BLOCKED", 409, "Source commercial snapshot cannot be revised", "SOURCE_SNAPSHOT_INVALID"));
}
