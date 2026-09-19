using System.Collections.Concurrent;

namespace UnicoreCRM.AI.Providers;

internal sealed class AiWorkspaceGuardrails
{
    private sealed class WorkspaceState(int concurrency)
    {
        internal SemaphoreSlim Concurrency { get; } = new(concurrency, concurrency);
        internal object Gate { get; } = new();
        internal Queue<DateTimeOffset> Starts { get; } = new();
    }
    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable { public void Dispose() => semaphore.Release(); }
    private readonly ConcurrentDictionary<string, WorkspaceState> states = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider; private readonly int concurrency; private readonly int requestsPerMinute;

    internal AiWorkspaceGuardrails(TimeProvider timeProvider, int concurrency, int requestsPerMinute)
    { this.timeProvider = timeProvider; this.concurrency = concurrency; this.requestsPerMinute = requestsPerMinute; }

    internal async Task<IDisposable> EnterAsync(string workspaceId, CancellationToken cancellationToken)
    {
        var state = states.GetOrAdd(workspaceId, _ => new(concurrency)); var now = timeProvider.GetUtcNow();
        lock (state.Gate)
        {
            while (state.Starts.TryPeek(out var value) && value <= now.AddMinutes(-1)) state.Starts.Dequeue();
            if (state.Starts.Count >= requestsPerMinute) throw new AiProviderRateLimitedException();
            state.Starts.Enqueue(now);
        }
        if (!await state.Concurrency.WaitAsync(TimeSpan.Zero, cancellationToken)) throw new AiProviderRateLimitedException();
        return new Lease(state.Concurrency);
    }
}
