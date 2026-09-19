using System.Net;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using UnicoreCRM.AI.Providers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

if (args.Length == 3 && args[0] is "dp-protect" or "dp-unprotect")
{
    var durableProvider = DataProtectionProvider.Create(new DirectoryInfo(args[1]), builder => builder.SetApplicationName("UnicoreCRM.AI"));
    var durableProtector = durableProvider.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1");
    if (args[0] == "dp-protect") File.WriteAllText(args[2], durableProtector.Protect("durability-verifier-secret"));
    else if (durableProtector.Unprotect(File.ReadAllText(args[2])) != "durability-verifier-secret") Environment.ExitCode = 2;
    return;
}

var request = new AiProviderRequest("test", "system", "question", "{}", "en", 0);
await VerifyGemini();
await VerifyOpenAi();
await VerifyRetryAndFallback();
await VerifyCircuitBreaker();
await VerifyDurableDataProtection();
Console.WriteLine("AI_PROVIDER_ADAPTER_VERIFIER_PASS");

async Task VerifyGemini()
{
    var handler = new StubHandler((message, _) =>
    {
        Assert(message.Headers.GetValues("x-goog-api-key").Single() == "gemini-secret", "Gemini auth header");
        Assert(!message.RequestUri!.ToString().Contains("gemini-secret", StringComparison.Ordinal), "Gemini secret absent from URL");
        return Json(HttpStatusCode.OK, """{"responseId":"gem-req","candidates":[{"content":{"parts":[{"text":"{\"summary\":\"ok\",\"suggestedNextAction\":null,\"attentionPoints\":[]}"}]}}],"usageMetadata":{"promptTokenCount":11,"candidatesTokenCount":7}}""");
    });
    var provider = new GeminiAiProvider(new HttpClient(handler) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System);
    var result = await provider.CompleteAsync("gemini-2.5-flash", "gemini-secret", request, CancellationToken.None);
    Assert(result.Provider == "GEMINI" && result.ProviderRequestId == "gem-req" && result.InputTokens == 11 && result.OutputTokens == 7, "Gemini result mapping");
    await ExpectFailure(new GeminiAiProvider(new HttpClient(new StubHandler((_, _) => Json(HttpStatusCode.TooManyRequests, "{}"))) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System), AiProviderFailure.RateLimited);
    await ExpectFailure(new GeminiAiProvider(new HttpClient(new StubHandler((_, _) => Json(HttpStatusCode.OK, "{bad"))) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System), AiProviderFailure.InvalidResponse);
}

async Task VerifyOpenAi()
{
    var handler = new StubHandler(async (message, _) =>
    {
        Assert(message.Headers.Authorization?.Scheme == "Bearer" && message.Headers.Authorization.Parameter == "openai-secret", "OpenAI auth header");
        var body = await message.Content!.ReadAsStringAsync();
        Assert(body.Contains("\"store\":false", StringComparison.Ordinal), "OpenAI retention disabled");
        var response = Json(HttpStatusCode.OK, """{"id":"resp_1","status":"completed","output":[{"content":[{"type":"output_text","text":"{\"summary\":\"ok\",\"suggestedNextAction\":null,\"attentionPoints\":[]}"}]}],"usage":{"input_tokens":13,"output_tokens":5}}""");
        response.Headers.Add("x-request-id", "oa-req"); return response;
    });
    var provider = new OpenAiProvider(new HttpClient(handler) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System);
    var result = await provider.CompleteAsync("gpt-5-mini", "openai-secret", request, CancellationToken.None);
    Assert(result.Provider == "OPENAI" && result.ProviderRequestId == "oa-req" && result.InputTokens == 13 && result.OutputTokens == 5, "OpenAI result mapping");
    await ExpectFailure(new OpenAiProvider(new HttpClient(new StubHandler((_, _) => Json(HttpStatusCode.Unauthorized, "{}"))) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System), AiProviderFailure.AuthenticationFailure);
    await ExpectFailure(new OpenAiProvider(new HttpClient(new StubHandler((_, _) => Json(HttpStatusCode.ServiceUnavailable, "{}"))) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System), AiProviderFailure.Unavailable);
}

async Task VerifyRetryAndFallback()
{
    var protection = new EphemeralDataProtectionProvider();
    var protectedPrimary = protection.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1").Protect("primary-key");
    var protectedFallback = protection.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1").Protect("fallback-key");
    var store = new FakeStore(new(new("GEMINI", "gemini-2.5-flash", "WORKSPACE", true, "OPENAI", "gpt-5-mini", "WORKSPACE", false), protectedPrimary, protectedFallback));
    var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
    var workspace = new FakeWorkspace(); var catalog = new AiProviderCatalog(configuration);
    var resolver = new WorkspaceAiProviderResolver(workspace, store, protection, configuration, catalog);
    var primary = new ScriptedAdapter("GEMINI", [AiProviderFailure.NetworkFailure, AiProviderFailure.Unavailable]);
    var fallback = new ScriptedAdapter("OPENAI", []);
    var ledger = new FakeLedger();
    var provider = new WorkspaceProductionAiProvider(resolver, [primary, fallback], workspace, ledger,
        new AiProviderCircuitBreaker(TimeProvider.System), new AiWorkspaceGuardrails(TimeProvider.System, 2, 10),
        new AiProviderRuntimeOptions(TimeSpan.FromSeconds(2)), TimeProvider.System);
    var result = await provider.CompleteAsync(request with { ExecutionId = "execution-failover" }, CancellationToken.None);
    Assert(result.Provider == "OPENAI", "Fallback provider completed");
    Assert(primary.Calls == 2 && fallback.Calls == 1, "Exactly one retry and one fallback");
    Assert(ledger.Attempts.Select(item => item.AttemptKind).SequenceEqual(["PRIMARY", "RETRY", "FALLBACK"]), "Attempt kinds are durable and ordered");
    Assert(ledger.Attempts.Select(item => item.ExecutionId).Distinct().Single() == "execution-failover", "Fallback preserves logical execution identity");
    Assert(primary.Requests.Concat(fallback.Requests).All(item => ReferenceEquals(item, primary.Requests[0])), "All provider attempts share one sanitized request snapshot");

    var timeoutPrimary = new BlockingAdapter("GEMINI"); var timeoutFallback = new ScriptedAdapter("OPENAI", []); var timeoutLedger = new FakeLedger();
    var timeoutProvider = Provider(resolver, workspace, timeoutLedger, timeoutPrimary, timeoutFallback, TimeSpan.FromMilliseconds(25));
    var timeoutResult = await timeoutProvider.CompleteAsync(request with { ExecutionId = "execution-timeout" }, CancellationToken.None);
    Assert(timeoutResult.Provider == "OPENAI" && timeoutPrimary.Calls == 2 && timeoutFallback.Calls == 1, "Actual per-attempt timeout retries then fails over");
    Assert(timeoutLedger.Attempts.Select(item => (item.AttemptKind, item.Status)).SequenceEqual(
        [("PRIMARY", "TIMEOUT"), ("RETRY", "TIMEOUT"), ("FALLBACK", "SUCCEEDED")]), "Actual timeout attempts are classified and ordered");

    var cancelPrimary = new BlockingAdapter("GEMINI"); var cancelFallback = new ScriptedAdapter("OPENAI", []); var cancelLedger = new FakeLedger();
    var cancelProvider = Provider(resolver, workspace, cancelLedger, cancelPrimary, cancelFallback, TimeSpan.FromSeconds(2));
    using (var callerCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25)))
    {
        try { await cancelProvider.CompleteAsync(request with { ExecutionId = "execution-cancel" }, callerCancellation.Token); }
        catch (OperationCanceledException) when (callerCancellation.IsCancellationRequested) { }
    }
    Assert(cancelPrimary.Calls == 1 && cancelFallback.Calls == 0 && cancelLedger.Attempts.Single().Status == "CANCELLED", "Caller cancellation never retries or fails over");

    var rateOnPrimary = new ScriptedAdapter("GEMINI", [AiProviderFailure.RateLimited, AiProviderFailure.RateLimited]);
    var rateOnFallback = new ScriptedAdapter("OPENAI", []);
    var rateOnResolver = Resolver(new(new("GEMINI", "gemini-2.5-flash", "WORKSPACE", true, "OPENAI", "gpt-5-mini", "WORKSPACE", true), protectedPrimary, protectedFallback), workspace, protection);
    var rateOn = await Provider(rateOnResolver, workspace, new FakeLedger(), rateOnPrimary, rateOnFallback).CompleteAsync(request with { ExecutionId = "execution-rate-on" }, CancellationToken.None);
    Assert(rateOn.Provider == "OPENAI" && rateOnPrimary.Calls == 2 && rateOnFallback.Calls == 1, "RATE_LIMITED policy ON retries and fails over");

    var rateOffPrimary = new ScriptedAdapter("GEMINI", [AiProviderFailure.RateLimited]); var rateOffFallback = new ScriptedAdapter("OPENAI", []);
    try { await Provider(resolver, workspace, new FakeLedger(), rateOffPrimary, rateOffFallback).CompleteAsync(request with { ExecutionId = "execution-rate-off" }, CancellationToken.None); }
    catch (AiProviderRateLimitedException) { }
    Assert(rateOffPrimary.Calls == 1 && rateOffFallback.Calls == 0, "RATE_LIMITED policy OFF does not retry or fail over");

    foreach (var terminal in new[] { AiProviderFailure.AuthenticationFailure, AiProviderFailure.SafetyRefusal })
    {
        var terminalPrimary = new ScriptedAdapter("GEMINI", [terminal]); var terminalFallback = new ScriptedAdapter("OPENAI", []);
        try { await Provider(resolver, workspace, new FakeLedger(), terminalPrimary, terminalFallback).CompleteAsync(request with { ExecutionId = $"execution-{terminal}" }, CancellationToken.None); }
        catch (Exception exception) when (exception is AiProviderUnavailableException or AiProviderSafetyRefusalException) { }
        Assert(terminalPrimary.Calls == 1 && terminalFallback.Calls == 0, $"{terminal} never retries or fails over");
    }

    var unavailablePrimary = new ScriptedAdapter("GEMINI", [AiProviderFailure.Unavailable, AiProviderFailure.Unavailable]);
    var unavailableFallback = new ScriptedAdapter("OPENAI", [AiProviderFailure.Unavailable]);
    try { await Provider(resolver, workspace, new FakeLedger(), unavailablePrimary, unavailableFallback).CompleteAsync(request with { ExecutionId = "execution-both-unavailable" }, CancellationToken.None); }
    catch (AiProviderUnavailableException) { }
    Assert(unavailablePrimary.Calls == 2 && unavailableFallback.Calls == 1, "Both providers unavailable produces one deterministic terminal failure");

    var refusalPrimary = new ScriptedAdapter("GEMINI", [AiProviderFailure.SafetyRefusal]); var unusedFallback = new ScriptedAdapter("OPENAI", []);
    var refusalProvider = new WorkspaceProductionAiProvider(resolver, [refusalPrimary, unusedFallback], workspace, new FakeLedger(),
        new AiProviderCircuitBreaker(TimeProvider.System), new AiWorkspaceGuardrails(TimeProvider.System, 2, 10),
        new AiProviderRuntimeOptions(TimeSpan.FromSeconds(2)), TimeProvider.System);
    try { await refusalProvider.CompleteAsync(request with { ExecutionId = "execution-refusal" }, CancellationToken.None); }
    catch (AiProviderSafetyRefusalException) { }
    Assert(refusalPrimary.Calls == 1 && unusedFallback.Calls == 0, "Safety refusal never retries or fails over");
}

WorkspaceProductionAiProvider Provider(WorkspaceAiProviderResolver valueResolver, FakeWorkspace valueWorkspace, FakeLedger valueLedger,
    IProductionAiProviderAdapter primary, IProductionAiProviderAdapter fallback, TimeSpan? timeout = null, AiProviderCircuitBreaker? breaker = null)
    => new(valueResolver, [primary, fallback], valueWorkspace, valueLedger, breaker ?? new AiProviderCircuitBreaker(TimeProvider.System),
        new AiWorkspaceGuardrails(TimeProvider.System, 2, 100), new AiProviderRuntimeOptions(timeout ?? TimeSpan.FromSeconds(2)), TimeProvider.System);

WorkspaceAiProviderResolver Resolver(ActiveWorkspaceAiPolicy active, FakeWorkspace valueWorkspace, IDataProtectionProvider protection)
    => new(valueWorkspace, new FakeStore(active), protection, new ConfigurationBuilder().AddInMemoryCollection().Build(),
        new AiProviderCatalog(new ConfigurationBuilder().AddInMemoryCollection().Build()));

async Task VerifyCircuitBreaker()
{
    var breaker = new AiProviderCircuitBreaker(TimeProvider.System, 3, TimeSpan.FromMilliseconds(5));
    const string scope = "workspace:provider:model";
    Assert(breaker.TryEnter(scope), "Circuit begins closed"); breaker.Failure(scope);
    Assert(breaker.TryEnter(scope), "Circuit stays closed below threshold"); breaker.Failure(scope);
    Assert(breaker.TryEnter(scope), "Circuit permits threshold attempt"); breaker.Failure(scope);
    Assert(!breaker.TryEnter(scope), "Circuit opens after repeated transient failures");
    await Task.Delay(10);
    Assert(breaker.TryEnter(scope), "Circuit admits one half-open probe");
    Assert(!breaker.TryEnter(scope), "Circuit permits only one half-open probe");
    breaker.Success(scope); Assert(breaker.TryEnter(scope), "Successful probe closes circuit");

    var protection = new EphemeralDataProtectionProvider();
    var active = new ActiveWorkspaceAiPolicy(new("GEMINI", "gemini-2.5-flash", "WORKSPACE", true, "OPENAI", "gpt-5-mini", "WORKSPACE", false),
        protection.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1").Protect("primary"), protection.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1").Protect("fallback"));
    var workspace = new FakeWorkspace(); var scopedBreaker = new AiProviderCircuitBreaker(TimeProvider.System, 3, TimeSpan.FromMinutes(1));
    var primaryScope = "workspace-a:GEMINI:gemini-2.5-flash";
    scopedBreaker.Failure(primaryScope); scopedBreaker.Failure(primaryScope); scopedBreaker.Failure(primaryScope);
    var skippedPrimary = new ScriptedAdapter("GEMINI", []); var successfulFallback = new ScriptedAdapter("OPENAI", []); var ledger = new FakeLedger();
    var result = await Provider(Resolver(active, workspace, protection), workspace, ledger, skippedPrimary, successfulFallback, breaker: scopedBreaker)
        .CompleteAsync(request with { ExecutionId = "execution-circuit-open" }, CancellationToken.None);
    Assert(result.Provider == "OPENAI" && skippedPrimary.Calls == 0 && successfulFallback.Calls == 1, "OPEN circuit skips primary transport and uses fallback");
    Assert(ledger.Attempts.Select(item => item.Status).SequenceEqual(["CIRCUIT_OPEN", "SUCCEEDED"]), "Circuit-open skip is auditable");
}

async Task VerifyDurableDataProtection()
{
    var root = Path.Combine(Path.GetTempPath(), "unicore-ai-dp-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root); var payload = Path.Combine(root, "credential.protected");
    try
    {
        await RunDataProtectionProcess("dp-protect", root, payload);
        await RunDataProtectionProcess("dp-unprotect", root, payload);
        Assert(Directory.EnumerateFiles(root, "key-*.xml").Any(), "Durable Data Protection key ring survives process restart");
    }
    finally { Directory.Delete(root, true); }
}

static async Task RunDataProtectionProcess(string mode, string keyRing, string payload)
{
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); start.ArgumentList.Add(mode); start.ArgumentList.Add(keyRing); start.ArgumentList.Add(payload);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Data Protection verifier process.");
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new InvalidOperationException("Data Protection verifier child process failed.");
}

async Task ExpectFailure(IProductionAiProviderAdapter provider, AiProviderFailure expected)
{
    try { await provider.CompleteAsync(provider.ProviderId == "GEMINI" ? "gemini-2.5-flash" : "gpt-5-mini", "secret", request, CancellationToken.None); }
    catch (AiProviderExecutionException exception) when (exception.Failure == expected) { return; }
    throw new InvalidOperationException($"Expected {expected} from {provider.ProviderId}.");
}

static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
static void Assert(bool value, string name) { if (!value) throw new InvalidOperationException($"Assertion failed: {name}"); }

sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
{
    public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> callback) : this((message, token) => Task.FromResult(callback(message, token))) { }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request, cancellationToken);
}

sealed class ScriptedAdapter(string providerId, Queue<AiProviderFailure> failures) : IProductionAiProviderAdapter
{
    public ScriptedAdapter(string providerId, IEnumerable<AiProviderFailure> failures) : this(providerId, new Queue<AiProviderFailure>(failures)) { }
    public string ProviderId => providerId; public int Calls { get; private set; } public List<AiProviderRequest> Requests { get; } = [];
    public Task<AiProviderAdapterResult> CompleteAsync(string model, string credential, AiProviderRequest request, CancellationToken cancellationToken)
    {
        Calls++; Requests.Add(request); if (failures.TryDequeue(out var failure)) throw new AiProviderExecutionException(failure, failure.ToString());
        return Task.FromResult(new AiProviderAdapterResult(providerId, model, "{\"summary\":\"ok\",\"suggestedNextAction\":null,\"attentionPoints\":[]}", TimeSpan.FromMilliseconds(1)));
    }
}
sealed class BlockingAdapter(string providerId) : IProductionAiProviderAdapter
{
    public string ProviderId => providerId; public int Calls { get; private set; }
    public async Task<AiProviderAdapterResult> CompleteAsync(string model, string credential, AiProviderRequest request, CancellationToken cancellationToken)
    {
        Calls++; await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    }
}
sealed class FakeWorkspace : ICurrentWorkspace
{
    public bool IsResolved => true; public TrustedWorkspaceContext Require() => new("workspace-a", "account-a", "member-a", "membership-a");
}
sealed class FakeLedger : IAiExecutionLedger
{
    public List<AiProviderAttemptEvidence> Attempts { get; } = [];
    public Task RecordAsync(AiExecutionEvidence evidence, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task RecordAttemptAsync(AiProviderAttemptEvidence evidence, CancellationToken cancellationToken) { Attempts.Add(evidence); return Task.CompletedTask; }
    public Task<AiWorkspaceUsageSummary> ReadWorkspaceSummaryAsync(string workspaceId, DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(new AiWorkspaceUsageSummary(0,0,0,0,0,0,null,null));
}
sealed class FakeStore(ActiveWorkspaceAiPolicy active) : IWorkspaceAiConfigurationStore
{
    public Task<ActiveWorkspaceAiPolicy?> FindActivePolicyAsync(string workspaceId, CancellationToken cancellationToken) => Task.FromResult<ActiveWorkspaceAiPolicy?>(active);
    public Task<WorkspaceAiConfigurationState?> FindAsync(string workspaceId, CancellationToken cancellationToken) => Task.FromResult<WorkspaceAiConfigurationState?>(null);
    public Task<string?> FindProtectedCredentialAsync(string workspaceId, bool fallback, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    public Task RecordTestOutcomeAsync(string workspaceId,string memberId,bool succeeded,string provider,string model,string correlationId,DateTimeOffset now,CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<AiConfigurationCommit> SaveDraftAsync(string workspaceId,string memberId,WorkspaceAiConfigurationDraft draft,long expectedVersion,string idempotencyKey,string fingerprint,string correlationId,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AiConfigurationCommit> SetCredentialAsync(string workspaceId,string memberId,bool fallback,string protectedCredential,long expectedVersion,string idempotencyKey,string fingerprint,string correlationId,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AiConfigurationCommit> SetValidationAsync(string workspaceId,string memberId,bool succeeded,long expectedVersion,string idempotencyKey,string fingerprint,string correlationId,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AiConfigurationCommit> ActivateAsync(string workspaceId,string memberId,long expectedVersion,string idempotencyKey,string fingerprint,string correlationId,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AiConfigurationCommit> DisableAsync(string workspaceId,string memberId,long expectedVersion,string idempotencyKey,string fingerprint,string correlationId,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
}
