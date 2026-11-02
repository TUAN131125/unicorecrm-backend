using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Orders.Contracts;
namespace UnicoreCRM.Sales.Orders.Infrastructure.Persistence;
internal sealed class OrderPaymentPlanSourceReader(OrdersDbContext db):IOrderPaymentPlanSourceReader
{
    public async Task<IReadOnlyList<OrderPaymentPlanSource>> ReadDraftsAsync(string? after,int limit,CancellationToken ct)
    {
        var query=db.Orders.AsNoTracking().Where(x=>x.State=="DRAFT" && x.ArchivedAt==null && x.SourceAcceptanceId!=null && x.PaymentAgreementSnapshotJson!=null);
        if(after is not null)query=query.Where(x=>string.Compare(x.OrderId,after)>0);
        var rows=await query.OrderBy(x=>x.OrderId).Take(Math.Clamp(limit,1,100)).ToArrayAsync(ct);
        return rows.Select(x=>new OrderPaymentPlanSource(x.WorkspaceId,x.OrderId,JsonSerializer.Serialize(new{
            id=x.SourceQuoteId,buyerRef=new{type=x.BuyerType,id=x.BuyerId},paymentAgreement=JsonSerializer.Deserialize<JsonElement>(x.PaymentAgreementSnapshotJson!)
        }))).ToArray();
    }
}
