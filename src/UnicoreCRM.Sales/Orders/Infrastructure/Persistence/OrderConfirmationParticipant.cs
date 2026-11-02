using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Sales.Orders.Contracts;
using UnicoreCRM.Sales.Orders.Domain;
using UnicoreCRM.Sales.Orders.Application.Common;
namespace UnicoreCRM.Sales.Orders.Infrastructure.Persistence;

internal sealed class OrderConfirmationParticipant(OrdersDbContext db,OrderAuthorization authorization):IOrderConfirmationParticipant
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<OrderConfirmationContext> ReadAsync(OwnerTransaction tx,string id,string request,string correlation,CancellationToken ct)
    {
        db.Database.SetDbConnection(tx.Connection,false);await db.Database.UseTransactionAsync(tx.Transaction,ct);
        var meta=new OrderRequestMetadata(request,correlation);
        var grant=await authorization.AuthorizeAsync(meta,ct,"orders.confirm");
        if(!grant.IsSuccess)throw new BusinessCommandException(grant.Error!.Code,grant.Error.Status);
        var access=grant.Value!;
        if(OrderFieldSecurity.FieldKeys.Where(f=>f is "buyerRef" or "lineItems" or "grandTotal" or "paymentAgreementSnapshot" or "acceptance" or "commercialSnapshotFingerprint" or "sourceQuoteResourceVersion")
            .Any(f=>!access.Authorization.CanRead(f)) || !access.Authorization.CanWrite("state") || !access.Authorization.CanWrite("confirmedAt"))
            throw new BusinessCommandException("ACCESS_DENIED",403);
        var order=await db.Orders.SingleOrDefaultAsync(x=>x.WorkspaceId==access.Trusted.WorkspaceId && x.OrderId==id,ct);
        if(order is null)throw new BusinessCommandException("RESOURCE_NOT_FOUND",404);
        var denied=await authorization.EnforceRecordAsync(access,order,"confirmOrderWithPaymentPlan",meta,ct,requireMutation:true);
        if(denied is not null)throw new BusinessCommandException(denied.Code,denied.Status);
        return new(access.Trusted.WorkspaceId,access.Trusted.MemberId,OrderProjection.Document(order),order.SourceAcceptanceId??"");
    }
    public Task ValidateSnapshotAsync(OrderConfirmationContext c,string source,CancellationToken ct)
    {
        var q=JsonSerializer.Deserialize<OrderAcceptedSnapshot>(source,Json)??throw new BusinessCommandException("ORDER_CONFIRMATION_BLOCKED");
        var o=db.Orders.Local.Single(x=>x.WorkspaceId==c.WorkspaceId && x.OrderId==c.Order.Id);
        try {AcceptedOrderValidation.Validate(q);}catch(Exception e)when(e is InvalidOperationException or JsonException){throw new BusinessCommandException("ORDER_CONFIRMATION_BLOCKED");}
        bool Same<T>(T a,string? b)=>b is not null && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(a,Json),JsonSerializer.Deserialize<JsonElement>(b));
        if(q.Id!=o.SourceQuoteId || q.ResourceVersion!=o.SourceQuoteResourceVersion || q.BuyerRef.Type!=o.BuyerType || q.BuyerRef.Id!=o.BuyerId
            || q.SourceDealId!=o.SourceDealId || q.Currency!=o.Currency || AcceptedOrderValidation.Amount(q.GrandTotal,q.Currency)!=o.GrandTotalAmount
            || AcceptedOrderValidation.Amount(q.Subtotal,q.Currency)!=o.SubtotalAmount || AcceptedOrderValidation.Amount(q.TaxTotal,q.Currency)!=o.TaxTotalAmount
            || AcceptedOrderValidation.Amount(q.DiscountTotal,q.Currency)!=o.DiscountTotalAmount
            || !Same(q.LineItems,o.LineItemsJson) || !Same(q.PaymentAgreement,o.PaymentAgreementSnapshotJson)
            || !Same(q.Adjustments,o.AdjustmentsJson??"null"))throw new BusinessCommandException("COMMERCIAL_SNAPSHOT_MISMATCH");
        return Task.CompletedTask;
    }
    public async Task<long> ConfirmAsync(OrderConfirmationContext c,string commandId,string notes,OrderConfirmationFulfillment? fulfillment,DateTimeOffset now,CancellationToken ct)
    {
        var o=db.Orders.Local.Single(x=>x.WorkspaceId==c.WorkspaceId && x.OrderId==c.Order.Id);
        if(fulfillment is not null)
        {
            try{_=OrderPersistedDocuments.ShippingAddress(JsonSerializer.Serialize(fulfillment.ShippingAddress,Json));}
            catch(InvalidOperationException){throw new BusinessCommandException("FULFILLMENT_INFORMATION_REQUIRED");}
            var grant=await authorization.AuthorizeAsync(new(commandId,commandId),ct,"orders.confirm");
            if(!grant.IsSuccess || new[]{"recipientName","recipientPhone","shippingAddress"}.Any(f=>!grant.Value!.Authorization.CanWrite(f)))throw new BusinessCommandException("ACCESS_DENIED",403);
        }
        o.Confirm(c.MemberId,notes,fulfillment,now);
        db.Add(new OrderOutboxMessage(commandId+"-order",c.WorkspaceId,o.OrderId,
            JsonSerializer.Serialize(new{commandId,actor=c.MemberId,confirmedAt=now,version=o.ResourceVersion,operationalReview=notes},Json),now,"ORDER_CONFIRMED"));
        await db.SaveChangesAsync(ct);return o.ResourceVersion;
    }
    public async Task<long> ConfirmAutomaticallyAsync(OrderConfirmationContext c,string commandId,string policyVersion,DateTimeOffset now,CancellationToken ct)
    {
        if(c.MemberId!="system:quote-policy")throw new BusinessCommandException("ACCESS_DENIED",403);
        var order=db.Orders.Local.Single(x=>x.WorkspaceId==c.WorkspaceId && x.OrderId==c.Order.Id);
        order.Confirm(c.MemberId,"",null,now,"WORKSPACE_POLICY",policyVersion);
        db.Add(new OrderOutboxMessage(commandId+"-order",c.WorkspaceId,order.OrderId,
            JsonSerializer.Serialize(new{commandId,confirmationOrigin="WORKSPACE_POLICY",policyVersion,sourceAcceptanceId=c.AcceptanceId,confirmedAt=now,version=order.ResourceVersion},Json),now,"ORDER_CONFIRMED"));
        await db.SaveChangesAsync(ct);return order.ResourceVersion;
    }

}
