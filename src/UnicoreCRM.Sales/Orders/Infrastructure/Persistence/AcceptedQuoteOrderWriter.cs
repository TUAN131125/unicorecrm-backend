using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Orders.Contracts;
using UnicoreCRM.Sales.Orders.Domain;
namespace UnicoreCRM.Sales.Orders.Infrastructure.Persistence;

internal sealed class AcceptedQuoteOrderWriter(OrdersDbContext db,TimeProvider clock):IAcceptedQuoteOrderWriter
{
    public async Task<string> CreateAsync(AcceptedQuoteOrderDraft source,CancellationToken ct)
    {
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var q=JsonSerializer.Deserialize<OrderAcceptedSnapshot>(source.SnapshotJson,json)??throw new InvalidOperationException("Missing accepted snapshot.");
        if(q.Id!=source.QuoteId || source.AcceptanceId.Length>128 || source.CommercialFingerprint.Length!=64)throw new InvalidOperationException("Source binding mismatch.");
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.SnapshotJson+"\n"+source.AcceptanceJson+"\n"+source.CommercialFingerprint)));
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var key="accepted-quote-order:"+source.WorkspaceId.ToLowerInvariant()+":"+source.QuoteId.ToLowerInvariant();
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={key},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @r<0 THROW 51001,'Order conversion lock unavailable',1;",ct);
        var existing=await db.Orders.AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==source.WorkspaceId && x.SourceQuoteId==source.QuoteId,ct);
        if(existing is not null)
        {
            var evidence=await db.Set<OrderCreationEvidence>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==source.WorkspaceId && x.AcceptanceId==source.AcceptanceId,ct);
            if(existing.SourceAcceptanceId!=source.AcceptanceId || evidence?.Fingerprint!=fingerprint)throw new InvalidOperationException("Source Quote already has a different Order conversion.");
            return existing.OrderId;
        }
        var now=clock.GetUtcNow();var order=Order.FromAccepted(source,q,now);db.Orders.Add(order);
        var payload=JsonSerializer.Serialize(new{source.AcceptanceId,source.QuoteId,order.OrderId,source.CommercialFingerprint,sourceAcceptance=JsonSerializer.Deserialize<JsonElement>(source.AcceptanceJson)},json);
        db.Add(new OrderCreationEvidence("order_audit_"+Guid.NewGuid().ToString("N"),source.WorkspaceId,source.QuoteId,source.AcceptanceId,order.OrderId,fingerprint,payload,now));
        db.Add(new OrderOutboxMessage("order_event_"+Guid.NewGuid().ToString("N"),source.WorkspaceId,order.OrderId,payload,now));
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return order.OrderId;
    }
    public async Task<OrderConfirmationContext> CreateInTransactionAsync(UnicoreCRM.BuildingBlocks.OwnerTransaction owner,AcceptedQuoteOrderDraft source,CancellationToken ct)
    {
        if(!ReferenceEquals(db.Database.GetDbConnection(),owner.Connection))db.Database.SetDbConnection(owner.Connection,false);
        await db.Database.UseTransactionAsync(owner.Transaction,ct);
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var q=JsonSerializer.Deserialize<OrderAcceptedSnapshot>(source.SnapshotJson,json)??throw new InvalidOperationException("Missing accepted snapshot.");
        if(q.Id!=source.QuoteId || source.AcceptanceId.Length>128 || source.CommercialFingerprint.Length!=64)throw new InvalidOperationException("Source binding mismatch.");
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.SnapshotJson+"\n"+source.AcceptanceJson+"\n"+source.CommercialFingerprint)));
        var key="accepted-quote-order:"+source.WorkspaceId.ToLowerInvariant()+":"+source.QuoteId.ToLowerInvariant();
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={key},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; IF @r<0 THROW 51001,'Order conversion lock unavailable',1;",ct);
        var existing=await db.Orders.AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==source.WorkspaceId && x.SourceQuoteId==source.QuoteId,ct);
        if(existing is not null)throw new UnicoreCRM.BuildingBlocks.BusinessCommandException("ORDER_CONVERSION_RECEIPT_REQUIRED");
        var now=clock.GetUtcNow();var order=Order.FromAccepted(source,q,now);db.Orders.Add(order);
        var payload=JsonSerializer.Serialize(new{source.AcceptanceId,source.QuoteId,order.OrderId,source.CommercialFingerprint,sourceAcceptance=JsonSerializer.Deserialize<JsonElement>(source.AcceptanceJson)},json);
        db.Add(new OrderCreationEvidence("order_audit_"+Guid.NewGuid().ToString("N"),source.WorkspaceId,source.QuoteId,source.AcceptanceId,order.OrderId,fingerprint,payload,now));
        db.Add(new OrderOutboxMessage("order_event_"+Guid.NewGuid().ToString("N"),source.WorkspaceId,order.OrderId,payload,now,"ORDER_CREATED_FROM_ACCEPTED_QUOTE"));
        await db.SaveChangesAsync(ct);return new(source.WorkspaceId,"system:quote-policy",Application.Common.OrderProjection.Document(order),source.AcceptanceId);
    }
}
