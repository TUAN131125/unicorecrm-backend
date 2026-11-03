using System.Text.Json;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Orders.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Workflows.Atomic.Contracts;
namespace UnicoreCRM.Workflows.Atomic.Application.ConfirmOrder;
internal static class ConfirmationRules
{
    internal static async Task ValidateAsync(OrderReadModel o,QuoteOrderConfirmationBinding binding,ConfirmOrderWithPaymentPlanRequest request,
        string workspaceId,DateTimeOffset now,IWorkspaceTimeZoneReader zones,CancellationToken ct)
    {
        if(o.State!="DRAFT" || o.ArchivedAt is not null)throw new BusinessCommandException("ORDER_CONFIRMATION_BLOCKED");
        if(binding.OperationalReviewRequired && (!request.OperationalReviewConfirmed || string.IsNullOrWhiteSpace(request.OperationalReviewNotes)))
            throw new BusinessCommandException("OPERATIONAL_APPROVAL_REQUIRED");
        var fulfillment=request.Fulfillment;
        if(fulfillment is not null && (string.IsNullOrWhiteSpace(fulfillment.RecipientName) || fulfillment.RecipientName.Length>240
            || string.IsNullOrWhiteSpace(fulfillment.RecipientPhone) || fulfillment.RecipientPhone.Length>80
            || fulfillment.ShippingAddress is not {Line1.Length:>0 and <=300,City.Length:>0 and <=120}
            || string.IsNullOrWhiteSpace(fulfillment.ShippingAddress.Line1) || string.IsNullOrWhiteSpace(fulfillment.ShippingAddress.City)))throw new BusinessCommandException("FULFILLMENT_INFORMATION_REQUIRED");
        // Products' canonical service code is lowercase. Keep legacy snapshots intact and do
        // not broaden this classification to arbitrary case variants or other product types.
        if(o.LineItems.Any(x=>x.ProductTypeSnapshot is not ("service" or "SERVICE" or "DIGITAL")) && fulfillment is null
            && (string.IsNullOrWhiteSpace(o.RecipientName) || string.IsNullOrWhiteSpace(o.RecipientPhone) || o.ShippingAddress is not {Line1.Length:>0,City.Length:>0}))
            throw new BusinessCommandException("FULFILLMENT_INFORMATION_REQUIRED");
        // A read summary is never approval authority. Until the credit owner admits a
        // consuming participant, postpaid agreements and required approvals fail closed.
        var agreement=o.PaymentAgreementSnapshot ?? throw new BusinessCommandException("PAYMENT_AGREEMENT_REQUIRED");
        DateOnly businessDate;
        try{var zone=await zones.ReadTimeZoneAsync(workspaceId,ct)??throw new TimeZoneNotFoundException();businessDate=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);}
        catch(Exception e)when(e is TimeZoneNotFoundException or InvalidTimeZoneException){throw new BusinessCommandException("WORKSPACE_TIMEZONE_UNAVAILABLE");}
        if(o.CreditPolicyEvaluation?.Status=="APPROVAL_REQUIRED" || agreement.GetProperty("lines").EnumerateArray().Any(l=>CreditRequired(l.GetProperty("dueRule"),businessDate)))
            throw new BusinessCommandException(request.CreditApprovalId is null?"CREDIT_APPROVAL_REQUIRED":"CREDIT_APPROVAL_NOT_FOUND");
        if(request.CreditApprovalId is not null)throw new BusinessCommandException("CREDIT_APPROVAL_NOT_FOUND");
        if(request.PaymentAccountId is not null)throw new BusinessCommandException("PAYMENT_ACCOUNT_AUTHORITY_REQUIRED");
    }
    private static bool CreditRequired(JsonElement rule,DateOnly businessDate)
    {
        var type=rule.GetProperty("type").GetString();
        if(type=="OPERATIONAL_PRECONDITION")return false;
        if(type=="FIXED_DATE")return DateOnly.ParseExact(rule.GetProperty("date").GetString()!,"yyyy-MM-dd")>businessDate;
        if(type!="EVENT_RELATIVE")return true;
        return rule.GetProperty("event").GetString()!="ORDER_CONFIRMED" || rule.GetProperty("offsetDays").GetInt32()>0;
    }
}
