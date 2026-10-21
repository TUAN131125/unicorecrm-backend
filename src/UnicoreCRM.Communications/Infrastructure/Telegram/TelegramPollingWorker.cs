using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Communications.Infrastructure.Persistence;

namespace UnicoreCRM.Communications.Infrastructure.Telegram;

internal sealed class TelegramPollingWorker(IServiceScopeFactory scopes, TelegramPollingOptions options,
    ILogger<TelegramPollingWorker> logger) : BackgroundService
{
    private DateTimeOffset nextRetentionCheck;
    internal async Task TickAsync(CancellationToken ct)
    {
        if (!options.Enabled) return;
        await using var scope = scopes.CreateAsyncScope();
        if (!scope.ServiceProvider.GetRequiredService<TelegramProviderAdmission>().Allowed) return;
        var db = scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>();
        var now = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT SYSDATETIMEOFFSET() AS [Value]").SingleAsync(ct);
        if (now >= nextRetentionCheck)
        {
            var cutoff = now.AddHours(-12);
            var risks = await db.TelegramPolling.AsNoTracking().Where(s => (s.LastSuccessfulPollAt ?? s.AdmittedAt) < cutoff)
                .OrderBy(s => s.LastSuccessfulPollAt ?? s.AdmittedAt).Take(32).Select(s => s.AccountId).ToListAsync(ct);
            foreach (var id in risks)
                logger.LogWarning("Telegram polling retention risk for account {AccountId}; pending provider updates have a limited recovery horizon", id);
            nextRetentionCheck = now.AddHours(1);
        }
        var due = await (from state in db.TelegramPolling.AsNoTracking()
                         join account in db.Accounts.AsNoTracking() on new { state.WorkspaceId, Id = state.AccountId } equals new { account.WorkspaceId, account.Id }
                         where state.Active && account.Status == "DRAFT" && account.ProviderKey == "telegram"
                            && account.ConfigurationVersion == state.ConnectionGeneration
                            && (state.RetryAfter == null || state.RetryAfter <= now) && (state.LeaseExpiry == null || state.LeaseExpiry <= now)
                         orderby state.LastAttemptAt, state.AccountId
                         select new { state.AccountId, state.WorkspaceId }).Take(32).ToListAsync(ct);
        // Bounded parallel polling: slow/error account does not block the other three slots.
        await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (item, token) =>
        {
            try
            {
                await using var accountScope = scopes.CreateAsyncScope();
                await accountScope.ServiceProvider.GetRequiredService<TelegramInboundCoordinator>().PollAsync(item.WorkspaceId, item.AccountId, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { logger.LogWarning("Telegram account polling deferred after a storage failure"); }
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Disabled worker does not even resolve persistence/credentials or create an HTTP request.
        if (!options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Telegram scheduler deferred after a storage failure"); }
            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
