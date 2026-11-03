using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Workflows.Atomic.Domain;
using UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;
namespace UnicoreCRM.Workflows.Atomic.Application.AcceptedQuoteCustomer;

internal sealed class RecoveryRunner(WorkflowsDbContext db,IAcceptedQuoteCustomerParticipant customers,TimeProvider clock)
{
    internal async Task RunAsync(CancellationToken ct)
    {
        var now=clock.GetUtcNow();
        var due=await db.Set<AcceptedQuoteCustomerTask>().AsNoTracking().Where(x=>x.State=="PENDING"&&x.NextAttemptAt<=now)
            .OrderBy(x=>x.NextAttemptAt).Select(x=>new{x.WorkspaceId,x.OrderId}).Take(25).ToArrayAsync(ct);
        foreach(var key in due)
        {
            await using var tx=await db.Database.BeginTransactionAsync(ct);
            var lockKey="accepted-quote-customer:"+key.WorkspaceId+":"+key.OrderId;
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource={lockKey},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=0;IF @r<0 THROW 51001,'Customer recovery already running',1;",ct);
            var task=await db.Set<AcceptedQuoteCustomerTask>().SingleAsync(x=>x.WorkspaceId==key.WorkspaceId&&x.OrderId==key.OrderId,ct);
            if(task.State!="PENDING"||task.NextAttemptAt>clock.GetUtcNow())continue;
            AcceptedQuoteCustomerResult result;
            try{result=await customers.ResolveAsync(key.WorkspaceId,key.OrderId,ct);}
            catch(Exception) when(!ct.IsCancellationRequested){result=new(false,null,null,"CUSTOMER_RECOVERY_TRANSIENT_FAILURE");}
            task.Record(result.Succeeded,result.CustomerId,result.Resolution,result.ErrorCode,clock.GetUtcNow());
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
    }
}
internal sealed class RecoveryService(IServiceScopeFactory scopes,IConfiguration configuration,ILogger<RecoveryService> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!configuration.GetValue<bool>("AcceptedQuoteCustomer:Enabled"))return;
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(5));
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            try{using var scope=scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<RecoveryRunner>().RunAsync(stoppingToken);}
            catch(Exception) when(!stoppingToken.IsCancellationRequested){logger.LogWarning("Accepted Quote Customer recovery deferred; durable tasks remain pending.");}
        }
    }
}
