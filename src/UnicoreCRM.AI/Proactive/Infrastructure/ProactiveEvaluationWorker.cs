using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnicoreCRM.AI.Proactive.Application;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

namespace UnicoreCRM.AI.Proactive.Infrastructure;

internal sealed class ProactiveEvaluationWorker : BackgroundService
{
    private readonly IServiceScopeFactory scopes;
    private readonly TimeProvider clock;
    private readonly ILogger<ProactiveEvaluationWorker> logger;
    private readonly TimeSpan leaseDuration;
    private readonly TimeSpan renewalInterval;

    public ProactiveEvaluationWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ProactiveEvaluationWorker> logger)
        : this(scopes, clock, logger, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1)) { }

    internal ProactiveEvaluationWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ProactiveEvaluationWorker> logger, TimeSpan leaseDuration, TimeSpan renewalInterval)
    {
        this.scopes = scopes;
        this.clock = clock;
        this.logger = logger;
        this.leaseDuration = leaseDuration;
        this.renewalInterval = renewalInterval;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { ProactiveMetrics.Failures.Add(1); logger.LogError(exception, "Proactive evaluation wake failed."); }
            await Task.Delay(TimeSpan.FromMinutes(5), clock, stoppingToken);
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IProactiveStore>();
        var scanNow = clock.GetUtcNow();
        var due = await store.ReadDuePoliciesAsync(scanNow, 25, ct);
        foreach (var policy in due)
        {
            await using var workspaceScope = scopes.CreateAsyncScope();
            var workspaceStore = workspaceScope.ServiceProvider.GetRequiredService<IProactiveStore>();
            var evaluator = workspaceScope.ServiceProvider.GetRequiredService<ProactiveWorkspaceEvaluator>();
            var zones = workspaceScope.ServiceProvider.GetRequiredService<IWorkspaceTimeZoneReader>();
            var leaseId = $"prolease_{Guid.NewGuid():N}";
            var claimNow = clock.GetUtcNow();
            if (!await store.TryClaimPolicyAsync(policy.WorkspaceId, policy.Version, leaseId, claimNow, claimNow.Add(leaseDuration), ct)) continue;
            using var evaluation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var heartbeat = RenewUntilCancelledAsync(policy.WorkspaceId, leaseId, evaluation);
            try
            {
                var zoneId = await zones.ReadTimeZoneAsync(policy.WorkspaceId, evaluation.Token);
                if (string.IsNullOrWhiteSpace(zoneId)) throw new InvalidOperationException("Authoritative Workspace timezone is unavailable.");
                await evaluator.EvaluateAsync(policy.WorkspaceId, $"proactive-eval-{leaseId}", token => RenewOrThrowAsync(policy.WorkspaceId, leaseId, token), evaluation.Token);
                var completedAt = clock.GetUtcNow();
                var next = NextLocalDay(completedAt, zoneId);
                await workspaceStore.CompletePolicyEvaluationAsync(policy.WorkspaceId, leaseId, completedAt, next, evaluation.Token);
                ProactiveMetrics.Workspaces.Add(1);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                ProactiveMetrics.Failures.Add(1);
                try
                {
                    await using var failureScope = scopes.CreateAsyncScope();
                    var failureStore = failureScope.ServiceProvider.GetRequiredService<IProactiveStore>();
                    await failureStore.RecordAuditAsync(new($"proaudit_{Guid.NewGuid():N}", policy.WorkspaceId, null, "EVALUATION_FAILED", null, null,
                        "{}", $"proactive-eval-{leaseId}", clock.GetUtcNow()), CancellationToken.None);
                }
                catch (Exception auditException)
                {
                    logger.LogError(auditException, "Proactive evaluation failure evidence could not be recorded WorkspaceId={WorkspaceId}.", policy.WorkspaceId);
                }
                logger.LogError(exception, "Proactive Workspace evaluation failed WorkspaceId={WorkspaceId}; lease will expire for retry.", policy.WorkspaceId);
            }
            finally
            {
                evaluation.Cancel();
                try { await heartbeat; } catch (OperationCanceledException) { }
            }
        }
        ProactiveMetrics.Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    internal static DateTimeOffset NextLocalDay(DateTimeOffset now, string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) throw new ArgumentException("Workspace timezone is required.", nameof(timeZoneId));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var nextLocal = local.Date.AddDays(1);
        return new DateTimeOffset(nextLocal, zone.GetUtcOffset(nextLocal)).ToUniversalTime();
    }

    private async Task RenewUntilCancelledAsync(string workspaceId, string leaseId, CancellationTokenSource evaluation)
    {
        try
        {
            while (!evaluation.IsCancellationRequested)
            {
                await Task.Delay(renewalInterval, clock, evaluation.Token);
                await RenewOrThrowAsync(workspaceId, leaseId, evaluation.Token);
            }
        }
        catch (OperationCanceledException) when (evaluation.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "Proactive evaluation lease renewal failed."); evaluation.Cancel(); }
    }

    private async Task RenewOrThrowAsync(string workspaceId, string leaseId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IProactiveStore>();
        var now = clock.GetUtcNow();
        if (!await store.RenewPolicyLeaseAsync(workspaceId, leaseId, now, now.Add(leaseDuration), ct))
            throw new InvalidOperationException("Proactive evaluation lease was lost.");
    }
}
