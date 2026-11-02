using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Sales.Orders.Contracts;
namespace UnicoreCRM.Sales.Orders.Domain;

internal sealed record OrderAcceptedSnapshot(string Id,string QuoteNumber,long ResourceVersion,OrderBuyerReference BuyerRef,
    IReadOnlyList<OrderLineReadModel> LineItems,IReadOnlyList<OrderCommercialAdjustmentReadModel>? Adjustments,
    OrderMoney Subtotal,OrderMoney DiscountTotal,OrderMoney TaxTotal,OrderMoney GrandTotal,string Currency,
    JsonElement PaymentAgreement,string? SourceDealId,string? SourceLeadId,string? ContactId,string? OwnerId,string? RecipientEmail,string? Notes,string? BuyerNameSnapshot);
internal static class AcceptedOrderValidation
{
    internal static decimal Amount(OrderMoney money,string currency)
    {
        if(money.Currency!=currency || !decimal.TryParse(money.Amount,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,out var value) || value<0)throw new InvalidOperationException("Invalid accepted commercial amount.");
        return value;
    }
    internal static void Validate(OrderAcceptedSnapshot q)
    {
        if(q.BuyerRef.Type is not ("CONTACT" or "ORGANIZATION_ACCOUNT") || string.IsNullOrWhiteSpace(q.BuyerRef.Id)
            || q.LineItems.Count is <1 or >500 || q.PaymentAgreement.ValueKind!=JsonValueKind.Object
            || q.PaymentAgreement.GetProperty("currency").GetString()!=q.Currency
            || q.PaymentAgreement.GetProperty("lines").GetArrayLength()<1)throw new InvalidOperationException("Invalid accepted Order source.");
        var subtotal=Amount(q.Subtotal,q.Currency);var discount=Amount(q.DiscountTotal,q.Currency);var tax=Amount(q.TaxTotal,q.Currency);var total=Amount(q.GrandTotal,q.Currency);
        var extra=0m;
        foreach(var a in q.Adjustments??[])
        {var value=Amount(a.Amount,q.Currency);extra+=a.Type=="DISCOUNT"?-value:value;}
        var lineSubtotal=q.LineItems.Sum(x=>Amount(x.LineSubtotal,q.Currency));
        var lineTax=q.LineItems.Sum(x=>Amount(x.LineTaxAmount,q.Currency));
        var lineDiscount=q.LineItems.Sum(x=>Amount(x.LineDiscountAmount,q.Currency));
        if(total<=0 || subtotal!=lineSubtotal || tax!=lineTax || discount!=lineDiscount+(q.Adjustments??[]).Where(x=>x.Type=="DISCOUNT").Sum(x=>Amount(x.Amount,q.Currency))
            || total!=q.LineItems.Sum(x=>Amount(x.LineTotal,q.Currency))+extra)
            throw new InvalidOperationException("Accepted Quote totals do not reconcile.");
    }
}
