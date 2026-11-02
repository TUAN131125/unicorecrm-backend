using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Documents;

internal sealed class QuoteProofScanWorker(IServiceScopeFactory scopes,IQuoteProofScanPolicyReader policies,IOptionsMonitor<QuoteProofScanOptions> options,TimeProvider clock,ILogger<QuoteProofScanWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var work=new List<(string Workspace,string Proof)>();
                foreach(var w in policies.EnabledWorkspaces())
                {
                    var policy=policies.Read(w);if(policy is null)continue;
                    using var scope=scopes.CreateScope();
                    var candidates=await scope.ServiceProvider.GetRequiredService<IQuoteProofScanStore>().CandidatesAsync(w,policy.Epoch,clock.GetUtcNow(),20,stoppingToken);
                    work.AddRange(candidates.Select(p=>(w,p)));
                }
                await Parallel.ForEachAsync(work,new ParallelOptions{MaxDegreeOfParallelism=2,CancellationToken=stoppingToken},async(item,ct)=>
                {
                    try{using var scope=scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<QuoteProofScanCoordinator>().RunAsync(item.Workspace,item.Proof,ct);}
                    catch(OperationCanceledException)when(ct.IsCancellationRequested){}
                    catch(Exception e){logger.LogWarning("Proof scan work failed ({FailureType}); durable lease recovery will retry within policy.",e.GetType().Name);}
                });
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){logger.LogWarning("Proof scan polling failed ({FailureType}).",e.GetType().Name);}
            var poll=2000;
            try{poll=Math.Clamp(options.CurrentValue.PollMilliseconds,500,30000);}
            catch(Exception e)when(e is InvalidOperationException or OptionsValidationException){/* Malformed scanner configuration disables scanning, not the host. */}
            try{await Task.Delay(poll,stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
        }
    }
}

internal sealed class DevelopmentProofScanCommitProbe(string? workspace=null,bool failAfterFlush=false,int pauseAfterClaimMilliseconds=0):IQuoteProofScanCommitProbe
{
    public void AfterResultFlush(string w){if(w==workspace && failAfterFlush)throw new InvalidOperationException("ISOLATED_SCAN_RESULT_ROLLBACK_PROBE");}
    public Task AfterClaimAsync(string w,CancellationToken ct)=>w==workspace && pauseAfterClaimMilliseconds>0?Task.Delay(Math.Clamp(pauseAfterClaimMilliseconds,1,120000),ct):Task.CompletedTask;
}
