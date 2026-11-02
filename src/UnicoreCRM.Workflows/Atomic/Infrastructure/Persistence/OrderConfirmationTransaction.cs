using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Workflows.Atomic.Application.Persistence;
using UnicoreCRM.Workflows.Atomic.Domain;
namespace UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;
internal sealed class OrderConfirmationTransaction(WorkflowsDbContext db):IOrderConfirmationTransaction
{
    public async Task<IOrderConfirmationSession> BeginAsync(string scope,string ws,string order,CancellationToken ct)
    {
        var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            foreach(var key in new[]{"order-confirm-intent:"+scope,"order-confirm:"+ws.ToLowerInvariant()+":"+order.ToLowerInvariant()})
                await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource={key},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;IF @r<0 THROW 51001,'Confirmation lock unavailable',1;",ct);
            return new Session(db,tx);
        }catch{await tx.DisposeAsync();throw;}
    }
    private sealed class Session(WorkflowsDbContext db,IDbContextTransaction tx):IOrderConfirmationSession
    {
        public OwnerTransaction OwnerTransaction {get;}=new(db.Database.GetDbConnection(),tx.GetDbTransaction());
        public Task<OrderConfirmationReceipt?> FindAsync(string scope,CancellationToken ct)=>db.Set<OrderConfirmationReceipt>().SingleOrDefaultAsync(x=>x.ScopeKey==scope,ct);
        public async Task CommitAsync(OrderConfirmationReceipt receipt,CancellationToken ct){db.Add(receipt);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
        public ValueTask DisposeAsync()=>tx.DisposeAsync();
    }
}
