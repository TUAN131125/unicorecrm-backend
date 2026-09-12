using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UnicoreCRM.Workflows.Atomic.Application.ConvertLeadToCustomer;

internal sealed class RecoveryService(IServiceScopeFactory scopes,TimeProvider timeProvider,ILogger<RecoveryService> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(30),timeProvider);
        do
        {
            try { await using var scope=scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<ILeadCustomerConversionRecoveryRunner>().ResumeDueAsync(Handler.RecoveryPrincipal,stoppingToken); }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Lead customer conversion recovery scan failed");}
        } while(await timer.WaitForNextTickAsync(stoppingToken));
    }
}
