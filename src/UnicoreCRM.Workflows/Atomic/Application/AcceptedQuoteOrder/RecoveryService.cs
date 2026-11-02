using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Orders.Contracts;
namespace UnicoreCRM.Workflows.Atomic.Application.AcceptedQuoteOrder;

// At-least-once delivery: Orders commits its unique source receipt first; the Quotes acknowledgment
// follows. A crash between the two commits replays the same Order, never a second creation.
internal sealed class RecoveryService(IServiceScopeFactory scopes,ILogger<RecoveryService> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope=scopes.CreateAsyncScope();
                var queue=scope.ServiceProvider.GetRequiredService<IAcceptedQuoteOrderQueue>();
                foreach(var source in await queue.PendingAsync(20,stoppingToken))
                {
                    try
                    {
                        // A fresh Orders scope prevents failed tracked changes contaminating subsequent retries.
                        await using var writerScope=scopes.CreateAsyncScope();
                        string order;
                        if(source.AutoConfirmRequested)
                            order=await writerScope.ServiceProvider.GetRequiredService<AutomaticConfirmation>().ExecuteAsync(source,stoppingToken);
                        else
                        {
                        var writer=writerScope.ServiceProvider.GetRequiredService<IAcceptedQuoteOrderWriter>();
                        order=await writer.CreateAsync(new(source.WorkspaceId,source.QuoteId,source.AcceptanceId,
                            source.SnapshotJson,source.CommercialFingerprint,source.AcceptanceJson,source.AcceptedAt),stoppingToken);
                        var plans=writerScope.ServiceProvider.GetRequiredService<UnicoreCRM.Billing.Payments.Contracts.IOrderPaymentPlanParticipant>();
                        await plans.EnsureDraftAsync(source.WorkspaceId,order,source.SnapshotJson,stoppingToken);
                        }
                        await queue.CompleteAsync(source.WorkspaceId,source.AcceptanceId,order,stoppingToken);
                    }
                    catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){throw;}
                    catch(UnicoreCRM.BuildingBlocks.BusinessCommandException error)
                    {
                        await queue.RetryAsync(source.WorkspaceId,source.AcceptanceId,error.Code,stoppingToken);
                        logger.LogInformation("Accepted Quote conversion is awaiting a required condition. Acceptance {AcceptanceId}",source.AcceptanceId);
                    }
                    catch(Exception)
                    {
                        await queue.RetryAsync(source.WorkspaceId,source.AcceptanceId,"ORDER_CREATION_RETRY_PENDING",stoppingToken);
                        logger.LogWarning("Accepted Quote Order creation will retry. Acceptance {AcceptanceId}",source.AcceptanceId);
                    }
                }
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("Accepted Quote Order recovery is temporarily unavailable.");}
            try{await Task.Delay(TimeSpan.FromSeconds(2),stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
        }
    }
}
