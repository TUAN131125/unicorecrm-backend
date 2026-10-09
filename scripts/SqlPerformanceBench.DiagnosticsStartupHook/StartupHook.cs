using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

// DOTNET_STARTUP_HOOKS requires this global type and method.
internal static class StartupHook
{
    private static Recorder? recorder;
    public static void Initialize()
    {
        try
        {
            var output = Environment.GetEnvironmentVariable("PERFBENCH_DIAGNOSTICS_OUTPUT");
            var run = Environment.GetEnvironmentVariable("PERFBENCH_RUN_ID");
            if (string.IsNullOrEmpty(output) || !Guid.TryParseExact(run, "N", out var runId)
                || !Path.IsPathFullyQualified(output)) return;
            output = Path.GetFullPath(output);
            for (var directory = new DirectoryInfo(output); directory != null; directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                    || File.Exists(Path.Combine(directory.FullName, ".git"))) return;
            Directory.CreateDirectory(output);
            // Unique output per fixture/process; never overwrite another run's evidence.
            var path = Path.Combine(output, $"runtime-{runId:N}-{Environment.ProcessId}.json");
            if (File.Exists(path)) return;
            recorder = new Recorder(path, runId);
            recorder.Start();
        }
        catch { /* Optional instrumentation cannot fail application startup. */ }
    }
}

internal sealed class Recorder(string path, Guid runId) : IObserver<DiagnosticListener>
{
    private const int Limit = 100_000;
    private readonly ConcurrentQueue<object> records = new();
    private readonly ConcurrentDictionary<string, long> eventCounts = new();
    private readonly ConcurrentDictionary<string, Request> requests = new();
    private readonly ConcurrentDictionary<string, Pending> pending = new();
    private readonly ConcurrentDictionary<string, Pending> readers = new();
    private readonly ConcurrentBag<IDisposable> subscriptions = new();
    private readonly object flushLock = new();
    private readonly object recordLock = new();
    private string currentPath = path;
    private string window = "initial";
    private DateTime windowStartedUtc = DateTime.UtcNow;
    private int ticks;
    private long rejectedRotations;
    private int count;
    private long dropped;
    private Timer? timer;
    private Counters? counters;
    private sealed record Request(string Id, string Trace, long Started);
    private sealed record Pending(Request Request, long Started, string Kind, string Module);

    public void Start()
    {
        // AllListeners immediately replays existing listeners as well as future ones.
        subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
        counters = new Counters(Add, CountEvent);
        timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }
    public void OnNext(DiagnosticListener listener)
    {
        try
        {
            if (listener.Name is "Microsoft.AspNetCore" or "Microsoft.EntityFrameworkCore")
                subscriptions.Add(listener.Subscribe(new Events(this), IsRelevant));
        }
        catch { }
    }
    public void OnError(Exception error) { }
    public void OnCompleted() { }
    private static bool IsRelevant(string name) => name is
        "Microsoft.AspNetCore.Hosting.HttpRequestIn.Start" or "Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop"
        or "Microsoft.EntityFrameworkCore.Database.Connection.ConnectionOpening"
        or "Microsoft.EntityFrameworkCore.Database.Connection.ConnectionOpened"
        or "Microsoft.EntityFrameworkCore.Database.Connection.ConnectionError"
        or "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuting"
        or "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted"
        or "Microsoft.EntityFrameworkCore.Database.Command.CommandError"
        or "Microsoft.EntityFrameworkCore.Database.Command.DataReaderDisposing";
    private static object? Property(object? payload, string name) => payload?.GetType().GetProperty(name)?.GetValue(payload);
    private void Observe(string name, object? payload)
    {
        CountEvent(name);
        var now = Stopwatch.GetTimestamp();
        var trace = Activity.Current?.TraceId.ToString();
        if (name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal))
        {
            if (Property(payload, "HttpContext") is not HttpContext context) return;
            trace ??= context.TraceIdentifier;
            if (name.EndsWith(".Start", StringComparison.Ordinal))
            {
                var id = context.Request.Headers["X-PerfBench-Request-Id"].ToString();
                if(string.IsNullOrEmpty(id))id=context.Request.Headers["X-Request-Id"].ToString();
                if (id.Length is < 10 or > 96 || !id.StartsWith("perf-load-", StringComparison.Ordinal)
                    || !id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return;
                if (requests.Count >= Limit) { Interlocked.Increment(ref dropped); return; }
                requests[trace] = new Request(id, trace, now);
            }
            else if (requests.TryRemove(trace, out var request))
                Add(new { type = "http", utc = DateTime.UtcNow, requestId = request.Id, traceId = request.Trace,
                    elapsedMs = Stopwatch.GetElapsedTime(request.Started, now).TotalMilliseconds,
                    statusCode = context.Response.StatusCode });
            return;
        }
        var connection = name.Contains(".Connection.", StringComparison.Ordinal);
        var commandId = Property(payload, "CommandId");
        var readerKey = "reader:" + commandId;
        if (name.EndsWith("DataReaderDisposing", StringComparison.Ordinal))
        {
            if (readers.TryRemove(readerKey, out var reader))
                Add(new { type = "reader-consumption", utc = DateTime.UtcNow,
                    requestId = reader.Request.Id, traceId = reader.Request.Trace,
                    kind = reader.Kind, module = reader.Module,
                    elapsedMs = Stopwatch.GetElapsedTime(reader.Started, now).TotalMilliseconds,
                    readCount = Property(payload, "ReadCount") is int readCount && readCount >= 0 ? readCount : (int?)null });
            return;
        }
        if (name.EndsWith("CommandError", StringComparison.Ordinal)) readers.TryRemove(readerKey, out _);
        var key = (connection ? "connection:" : "command:") + (connection ? Property(payload, "ConnectionId") : commandId);
        if (name.EndsWith("Opening", StringComparison.Ordinal) || name.EndsWith("Executing", StringComparison.Ordinal))
        {
            if (trace == null || !requests.TryGetValue(trace, out var request)) return;
            if (pending.Count >= Limit) { Interlocked.Increment(ref dropped); return; }
            var command = Property(payload, "Command") as DbCommand;
            var classification = Classify(command?.CommandText);
            pending[key] = new Pending(request, now, connection ? "connection-acquisition" : classification.Kind, classification.Module);
        }
        else if (pending.TryRemove(key, out var started))
        {
            if (name.EndsWith("CommandExecuted", StringComparison.Ordinal)
                && Property(payload, "Result") is DbDataReader)
            {
                if (readers.Count < Limit) readers[readerKey] = started with { Started = now };
                else Interlocked.Increment(ref dropped);
            }
            var duration = Property(payload, "Duration") is TimeSpan measured ? measured.TotalMilliseconds
                : Stopwatch.GetElapsedTime(started.Started, now).TotalMilliseconds;
            Add(new { type = connection ? "connection" : "command", utc = DateTime.UtcNow,
                requestId = started.Request.Id, traceId = started.Request.Trace,
                kind = started.Kind, module = started.Module, elapsedMs = duration,
                failed = name.EndsWith("Error", StringComparison.Ordinal) });
        }
    }
    private static (string Kind, string Module) Classify(string? sql)
    {
        if (sql == null) return ("unknown", "unknown");
        var module = sql.Contains("Contacts", StringComparison.OrdinalIgnoreCase) ? "contacts"
            : sql.Contains("Leads", StringComparison.OrdinalIgnoreCase) ? "leads"
            : sql.Contains("Tasks", StringComparison.OrdinalIgnoreCase) ? "tasks" : "other";
        var text = sql.TrimStart();
        // Inspect the outer SELECT before aggregate tokens: follow-up joins can
        // contain an inner Tasks GROUP BY in both count and page commands.
        var outerCount = Regex.IsMatch(text, @"\A\s*(?:--[^\r\n]*(?:\r?\n|\z)\s*)*SELECT\s+COUNT(?:_BIG)?\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var outerPage = Regex.IsMatch(text, @"\A\s*(?:--[^\r\n]*(?:\r?\n|\z)\s*)*SELECT\s+TOP\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var kind = text.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("UPDATE [", StringComparison.OrdinalIgnoreCase)
            || sql.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase) ? "write"
            : outerCount ? "count" : outerPage ? "page"
            : sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("SUM(", StringComparison.OrdinalIgnoreCase) ? "summary"
            : sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase) || sql.Contains("COUNT_BIG(", StringComparison.OrdinalIgnoreCase) ? "count" : "page";
        return (kind, module);
    }
    private void CountEvent(string name) => eventCounts.AddOrUpdate(name, 1, (_, previous) => previous + 1);
    private void Add(object record)
    {
        lock (recordLock)
        {
            if (++count <= Limit) records.Enqueue(record);
            else Interlocked.Increment(ref dropped);
        }
    }
    private void Tick()
    {
        try
        {
            lock (flushLock)
            {
                var controlPath = Path.Combine(Path.GetDirectoryName(currentPath)!, "window.json");
                if (File.Exists(controlPath) && new FileInfo(controlPath).Length <= 4096)
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(controlPath));
                    var root = document.RootElement;
                    if (root.TryGetProperty("runId", out var ownership)
                        && ownership.GetString() == runId.ToString("N")
                        && root.TryGetProperty("window", out var label))
                    {
                        var next = label.GetString();
                        if (next != null && next != window && next.Length is > 0 and <= 64
                            && next.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
                        {
                            var nextPath = Path.Combine(Path.GetDirectoryName(currentPath)!,
                                $"runtime-{runId:N}-{Environment.ProcessId}-{next}.json");
                            if (File.Exists(nextPath) || File.Exists(nextPath + ".tmp")) ++rejectedRotations;
                            else
                            {
                                lock (recordLock)
                                {
                                    // Rotation is between drained stages; prevent records arriving
                                    // between the final snapshot and queue reset from disappearing.
                                    if (!Flush()) return;
                                    records.Clear();
                                    count = 0;
                                    dropped = 0;
                                    window = next;
                                    currentPath = nextPath;
                                    windowStartedUtc = DateTime.UtcNow;
                                }
                                Flush();
                            }
                        }
                    }
                }
            }
        }
        catch { }
        if (Interlocked.Increment(ref ticks) % 5 == 0) Flush();
    }
    private bool Flush()
    {
        try
        {
            lock (flushLock)
            {
                object[] captured;
                long droppedSnapshot;
                lock (recordLock)
                {
                    captured = records.ToArray();
                    droppedSnapshot = dropped;
                }
                var temporary = currentPath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(new { runId, processId = Environment.ProcessId,
                    window, windowStartedUtc, snapshotUtc = DateTime.UtcNow,
                    recordLimit = Limit, droppedRecords = droppedSnapshot,
                    observedEventCountsScope = "process-cumulative", rejectedRotations,
                    observedEvents = eventCounts.ToArray(), pendingOperations = pending.Count,
                    pendingReaders = readers.Count,
                    activeRequests = requests.Count, records = captured }));
                File.Move(temporary, currentPath, true);
                return true;
            }
        }
        catch { return false; }
    }
    private sealed class Events(Recorder owner) : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value) { try { owner.Observe(value.Key, value.Value); } catch { } }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}

internal sealed class Counters(Action<object> add, Action<string> eventSeen) : EventListener
{
    protected override void OnEventSourceCreated(EventSource source)
    {
        try
        {
            if (source.Name is "System.Runtime" or "Microsoft.Data.SqlClient.EventSource")
                EnableEvents(source, EventLevel.LogAlways, EventKeywords.None,
                    new Dictionary<string, string?> { ["EventCounterIntervalSec"] = "1" });
        }
        catch { }
    }
    protected override void OnEventWritten(EventWrittenEventArgs data)
    {
        try
        {
            if (data.EventName != "EventCounters" || data.Payload?.Count != 1
                || data.Payload[0] is not IDictionary<string, object> counter) return;
            // EventListener base constructor can call overrides before primary constructor fields initialize.
            if (add == null || eventSeen == null) return;
            var name = counter.TryGetValue("Name", out var named) ? named as string : null;
            if (name == null) return;
            var isIncrement = counter.TryGetValue("Increment", out var value);
            if (!isIncrement && !counter.TryGetValue("Mean", out value)) return;
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (!double.IsFinite(number)) return;
            eventSeen(data.EventSource.Name + ".EventCounters." + name);
            add(new { type = "counter", utc = DateTime.UtcNow, source = data.EventSource.Name,
                name, value = number, aggregation = isIncrement ? "increment" : "mean",
                displayUnits = counter.TryGetValue("DisplayUnits",out var units) ? units as string : null,
                intervalSeconds = counter.TryGetValue("IntervalSec", out var interval) ? Convert.ToDouble(interval, CultureInfo.InvariantCulture) : (double?)null });
        }
        catch { }
    }
}
