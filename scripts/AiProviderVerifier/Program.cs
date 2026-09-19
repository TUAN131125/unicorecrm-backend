using System.Net;
using System.Text;
using UnicoreCRM.AI.Providers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

var request = new AiProviderRequest("test", "system", "question", "{}", "en", 0);
await VerifyGemini();
await VerifyOpenAi();
await VerifyRetryAndFallback();
await VerifyCircuitBreaker();
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

    var refusalPrimary = new ScriptedAdapter("GEMINI", [AiProviderFailure.SafetyRefusal]); var unusedFallback = new ScriptedAdapter("OPENAI", []);
    var refusalProvider = new WorkspaceProductionAiProvider(resolver, [refusalPrimary, unusedFallback], workspace, new FakeLedger(),
        new AiProviderCircuitBreaker(TimeProvider.System), new AiWorkspaceGuardrails(TimeProvider.System, 2, 10),
        new AiProviderRuntimeOptions(TimeSpan.FromSeconds(2)), TimeProvider.System);
    try { await refusalProvider.CompleteAsync(request with { ExecutionId = "execution-refusal" }, CancellationToken.None); }
    catch (AiProviderSafetyRefusalException) { }
    Assert(refusalPrimary.Calls == 1 && unusedFallback.Calls == 0, "Safety refusal never retries or fails over");
}

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
    public string ProviderId => providerId; public int Calls { get; private set; }
    public Task<AiProviderAdapterResult> CompleteAsync(string model, string credential, AiProviderRequest request, CancellationToken cancellationToken)
    {
        Calls++; if (failures.TryDequeue(out var failure)) throw new AiProviderExecutionException(failure, failure.ToString());
        return Task.FromResult(new AiProviderAdapterResult(providerId, model, "{\"summary\":\"ok\",\"suggestedNextAction\":null,\"attentionPoints\":[]}", TimeSpan.FromMilliseconds(1)));
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
