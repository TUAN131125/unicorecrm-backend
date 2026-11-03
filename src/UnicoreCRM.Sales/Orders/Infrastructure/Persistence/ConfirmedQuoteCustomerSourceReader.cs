using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Orders.Contracts;
namespace UnicoreCRM.Sales.Orders.Infrastructure.Persistence;

internal sealed class ConfirmedQuoteCustomerSourceReader(OrdersDbContext db):IConfirmedQuoteCustomerSourceReader
{
    public async Task<ConfirmedQuoteCustomerSource?> ReadAsync(string workspaceId,string orderId,CancellationToken ct)
    {
        var o=await db.Orders.AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==workspaceId&&x.OrderId==orderId,ct);
        if(o is null||o.State!="CONFIRMED"||o.ArchivedAt is not null||o.SourceQuoteId is null||o.SourceAcceptanceId is null
            ||o.AcceptanceJson is null||o.BuyerType is not("CONTACT" or "ORGANIZATION_ACCOUNT"))return null;
        using var document=JsonDocument.Parse(o.AcceptanceJson);var a=document.RootElement;
        if(!a.TryGetProperty("acceptanceId",out var id)||id.GetString()!=o.SourceAcceptanceId
            ||!a.TryGetProperty("quoteId",out var quote)||quote.GetString()!=o.SourceQuoteId
            ||!a.TryGetProperty("source",out var source)||source.GetString() is not("CUSTOMER_LINK" or "STAFF_RECORDED"))return null;
        return new(workspaceId,o.OrderId,o.SourceQuoteId,o.SourceAcceptanceId,o.BuyerType,o.BuyerId,o.OwnerId);
    }
}
