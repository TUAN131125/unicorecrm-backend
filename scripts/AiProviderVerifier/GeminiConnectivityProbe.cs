using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.AI.Providers;

// Isolated console diagnostic, not a provider acceptance test or a production EventListener.
// Event payloads may contain paths and exception messages: retain event names/timing only.
internal sealed class GeminiConnectivityProbe : EventListener
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<Checkpoint> checkpoints = [];
    private static readonly HashSet<string> AllowedEvents =
    [
        "RequestStart", "ResolutionStart", "ResolutionStop", "ResolutionFailed",
        "ConnectStart", "ConnectStop", "ConnectFailed", "HandshakeStart", "HandshakeStop", "HandshakeFailed",
        "ConnectionEstablished", "RequestLeftQueue", "RequestHeadersStart", "RequestHeadersStop",
        "RequestContentStart", "RequestContentStop", "ResponseHeadersStart", "ResponseHeadersStop",
        "RequestFailed", "RequestStop"
    ];

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name is "System.Net.Http" or "System.Net.NameResolution" or "System.Net.Sockets" or "System.Net.Security")
            EnableEvents(source, EventLevel.Verbose);
    }

    protected override void OnEventWritten(EventWrittenEventArgs data)
    {
        // EventListener can receive callbacks while its base constructor is running.
        if (checkpoints is null || data.EventName is null || !AllowedEvents.Contains(data.EventName)) return;
        lock (checkpoints) checkpoints.Add(new(data.EventSource.Name, data.EventName, clock.ElapsedMilliseconds));
    }

    internal static async Task RunAsync()
    {
        // No appsettings, secrets, environment credential, Workspace or DataProtection service resolution.
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddLogging(); // No logger providers: no headers, body or unfiltered HTTP trace is emitted.
        services.AddProvidersModule(config, new TestHostEnvironment());
        using var provider = services.BuildServiceProvider();
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GeminiAiProvider));
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(GeminiAiProvider));
        while (handler is DelegatingHandler delegating && delegating.InnerHandler is not null) handler = delegating.InnerHandler;
        var sockets = handler as SocketsHttpHandler;
        var target = new Uri("https://generativelanguage.googleapis.com/");

        using var trace = new GeminiConnectivityProbe();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var startedAt = DateTimeOffset.UtcNow;
        string outcome;
        int? status = null;
        string? errorType = null, networkError = null, socketError = null, proxyMode = null;
        try
        {
            // Default proxy selection is part of this probe, not bypassed or replaced.
            trace.AddCheckpoint("Probe", "ProxySelectionStart");
            var proxy = sockets?.Proxy ?? HttpClient.DefaultProxy;
            var effectiveProxy = await Task.Run(() => proxy.GetProxy(target)).WaitAsync(deadline.Token);
            trace.AddCheckpoint("Probe", "ProxySelectionStop");
            proxyMode = sockets?.UseProxy == false ? "DISABLED_BY_HANDLER"
                : effectiveProxy is null || effectiveProxy == target ? "DIRECT" : "PROXY";
            using var request = new HttpRequestMessage(HttpMethod.Head, target);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            status = (int)response.StatusCode;
            outcome = "HTTP_HEADERS_RECEIVED";
        }
        catch (Exception exception)
        {
            outcome = "FAILED";
            errorType = exception.GetType().Name;
            if (exception is HttpRequestException failure) networkError = failure.HttpRequestError.ToString();
            for (Exception? inner = exception; inner is not null; inner = inner.InnerException)
                if (inner is SocketException socket) socketError = socket.SocketErrorCode.ToString();
            Environment.ExitCode = 1;
        }
        Checkpoint[] events;
        lock (trace.checkpoints) events = [.. trace.checkpoints];
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Check = "GEMINI_NAMED_HTTP_CLIENT_HEAD_ROOT_NO_CREDENTIAL_NO_BODY",
            Scope = "Current invoking process; source-default client configuration, not historical Backend process",
            StartedAtUtc = startedAt,
            Runtime = RuntimeInformation.FrameworkDescription,
            Outcome = outcome, HttpStatus = status, ElapsedMs = trace.clock.ElapsedMilliseconds,
            DeadlineElapsed = deadline.IsCancellationRequested, ErrorType = errorType, NetworkError = networkError, SocketError = socketError,
            PrimaryHandler = handler.GetType().Name,
            HttpClientTimeoutSeconds = http.Timeout.TotalSeconds,
            ConnectTimeoutSeconds = sockets?.ConnectTimeout == Timeout.InfiniteTimeSpan ? (double?)null : sockets?.ConnectTimeout.TotalSeconds,
            ConnectTimeoutInfinite = sockets?.ConnectTimeout == Timeout.InfiniteTimeSpan,
            sockets?.UseProxy, ProxyMode = proxyMode,
            CustomCertificateValidation = sockets?.SslOptions.RemoteCertificateValidationCallback is not null,
            HttpVersion = http.DefaultRequestVersion.ToString(), VersionPolicy = http.DefaultVersionPolicy.ToString(),
            Events = events
        }));
    }

    private void AddCheckpoint(string source, string name)
    {
        lock (checkpoints) checkpoints.Add(new(source, name, clock.ElapsedMilliseconds));
    }

    private sealed record Checkpoint(string Source, string Event, long ElapsedMs);
}
