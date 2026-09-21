using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnicoreCRM.AI.Proactive.Application;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

namespace UnicoreCRM.AI.Proactive.Infrastructure;

internal sealed class ProactiveEvaluationWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ProactiveEvaluationWorker> logger) : BackgroundService
{
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
        var evaluator = scope.ServiceProvider.GetRequiredService<ProactiveWorkspaceEvaluator>();
        var zones = scope.ServiceProvider.GetRequiredService<IWorkspaceTimeZoneReader>();
        var now = clock.GetUtcNow();
        var due = await store.ReadDuePoliciesAsync(now, 25, ct);
        foreach (var policy in due)
        {
            var leaseId = $"prolease_{Guid.NewGuid():N}";
            if (!await store.TryClaimPolicyAsync(policy.WorkspaceId, policy.Version, leaseId, now, now.AddMinutes(15), ct)) continue;
            var zoneId = await zones.ReadTimeZoneAsync(policy.WorkspaceId, ct) ?? "UTC";
            var next = NextLocalDay(now, zoneId);
            await evaluator.EvaluateAsync(policy.WorkspaceId, $"proactive-eval-{leaseId}", ct);
            await store.CompletePolicyEvaluationAsync(policy.WorkspaceId, leaseId, now, next, ct);
            ProactiveMetrics.Workspaces.Add(1);
        }
        ProactiveMetrics.Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    internal static DateTimeOffset NextLocalDay(DateTimeOffset now, string timeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var nextLocal = local.Date.AddDays(1);
        return new DateTimeOffset(nextLocal, zone.GetUtcOffset(nextLocal)).ToUniversalTime();
    }
}
