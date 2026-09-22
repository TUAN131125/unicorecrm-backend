using System.Diagnostics.Metrics;
using UnicoreCRM.Crm.Customers.Contracts;

namespace UnicoreCRM.AI.Proactive.Application;

internal sealed class ProactiveWorkspaceEvaluator(IProactiveCustomerHealthReader customers, CustomerHealthRiskReconciler reconciler)
{
    internal async Task<int> EvaluateAsync(string workspaceId, string correlationId, Func<CancellationToken, Task> renewLease, CancellationToken ct)
    {
        string? cursor = null;
        var count = 0;
        do
        {
            var page = await customers.ReadPageAsync(workspaceId, cursor, 250, ct);
            await renewLease(ct);
            await reconciler.ReconcilePageAsync(workspaceId, page.Items, correlationId, ct);
            await renewLease(ct);
            count += page.Items.Count;
            cursor = page.NextCursor;
        } while (cursor is not null);
        ProactiveMetrics.Customers.Add(count);
        return count;
    }
}

internal static class ProactiveMetrics
{
    private static readonly Meter Meter = new("UnicoreCRM.AI.Proactive", "1.0");
    internal static readonly Counter<long> Workspaces = Meter.CreateCounter<long>("evaluation_workspaces");
    internal static readonly Counter<long> Customers = Meter.CreateCounter<long>("evaluation_customers");
    internal static readonly Counter<long> Failures = Meter.CreateCounter<long>("evaluation_failures");
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("evaluation_duration", "ms");
}
