using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Sales.Orders.Contracts;
using UnicoreCRM.Billing.Payments.Contracts;
namespace UnicoreCRM.Workflows.Atomic.Application.AcceptedQuoteOrder;

// Existing O1 drafts may already have an acknowledged acceptance. Bounded keyset
// recovery prepares their missing plan without changing acceptance or Order state.
internal sealed class DraftPlanRecoveryService(IServiceScopeFactory scopes,ILogger<DraftPlanRecoveryService> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        string? cursor=null;
        while(!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();
                var sources=await scope.ServiceProvider.GetRequiredService<IOrderPaymentPlanSourceReader>().ReadDraftsAsync(cursor,20,ct);
                foreach(var source in sources)
                {
                    try
                    {
                        await using var writer=scopes.CreateAsyncScope();
                        await writer.ServiceProvider.GetRequiredService<IOrderPaymentPlanParticipant>().EnsureDraftAsync(source.WorkspaceId,source.OrderId,source.SnapshotJson,ct);
                    }
                    catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
                    catch(Exception){logger.LogWarning("Draft Order Payment Plan preparation will retry. Order {OrderId}",source.OrderId);}
                }
                cursor=sources.Count==20?sources[^1].OrderId:null;
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("Draft Order Payment Plan recovery is temporarily unavailable.");}
            try{await Task.Delay(TimeSpan.FromSeconds(2),ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
        }
    }
}
