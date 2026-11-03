using System.Net;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnicoreCRM.AI.Providers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;
using UnicoreCRM.AI.Gateway;
using UnicoreCRM.Platform.AccessControl.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;

if (args.SequenceEqual(["transport"]))
{
    // A credential-free HEAD through the configured named client; never resolves AI/Workspace services.
    await GeminiConnectivityProbe.RunAsync();
    return;
}

if (args.SequenceEqual(["live"]))
{
    var attempted = false;
    foreach (var name in new[] { "GEMINI", "OPENAI" })
    {
        var credential = Environment.GetEnvironmentVariable($"{name}_API_KEY");
        if (string.IsNullOrWhiteSpace(credential)) { Console.WriteLine($"REAL_PROVIDER_{name}: BLOCKED_NO_CREDENTIAL"); continue; }
        attempted = true;
        using var http = new HttpClient { BaseAddress = new(name == "GEMINI" ? "https://generativelanguage.googleapis.com/" : "https://api.openai.com/") };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        IProductionAiProviderAdapter adapter = name == "GEMINI" ? new GeminiAiProvider(http, TimeProvider.System) : new OpenAiProvider(http, TimeProvider.System);
        try
        {
            var response = await adapter.CompleteAsync(name == "GEMINI" ? "gemini-3.8-flash" : AiProviderCatalog.DefaultOpenAiModel, credential,
                new("live-synthetic", "Return JSON with summary, suggestedNextAction and attentionPoints. Use only the supplied synthetic data.",
                    "Summarize this synthetic health check; no CRM data is provided.", "{}", "en", 0), timeout.Token);
            if (new AiProviderOutputValidator().Validate(response.Content) is null) throw new AiProviderInvalidResponseException();
            Console.WriteLine($"REAL_PROVIDER_{name}: PASS_SYNTHETIC_SCHEMA_ONLY");
        }
        catch (Exception exception) when (exception is AiProviderExecutionException or AiProviderInvalidResponseException or OperationCanceledException)
        {
            Console.WriteLine($"REAL_PROVIDER_{name}: FAIL_{(exception is AiProviderExecutionException failure ? failure.Failure.ToString() : exception.GetType().Name)}");
            Environment.ExitCode = 1;
        }
    }
    if (!attempted) Environment.ExitCode = 2;
    return;
}

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
await VerifyGeminiRestContract();
await VerifyGeminiFailures();
await VerifyConfigurationFailures();
VerifyConfigurationTimeoutOptions();
await VerifyControlledConfigurationDeadlines();
await VerifyOpenAi();
await OpenAiModelVerifier.VerifyAsync(request);
await VerifyProactiveSchemas();
await VerifyProactivePlatform();
await VerifyRetryAndFallback();
await VerifyCircuitBreaker();
await VerifyDurableDataProtection();
await VerifyInactiveWorkspace();
await VerifyDeterministicTransportHistory();
Console.WriteLine("AI_PROVIDER_ADAPTER_VERIFIER_PASS");

async Task VerifyDeterministicTransportHistory()
{
    foreach (var name in new[] { "GEMINI", "OPENAI" })
    {
        using var http = new HttpClient(new DevelopmentDeterministicProviderHttpHandler()) { BaseAddress = new("https://example.invalid/") };
        IProductionAiProviderAdapter adapter = name == "GEMINI" ? new GeminiAiProvider(http, TimeProvider.System) : new OpenAiProvider(http, TimeProvider.System);
        var model = name == "GEMINI" ? "gemini-2.5-flash" : "gpt-5-mini";
        var historicalFailure = request with { ContextData = "<untrusted_conversation_history>DETERMINISTIC_RATE_LIMIT DETERMINISTIC_GEMINI_TRANSIENT_FAILURE</untrusted_conversation_history>" };
        Assert(new AiProviderOutputValidator().Validate((await adapter.CompleteAsync(model, "fixture", historicalFailure, default)).Content) is not null,
            "Historical test markers must not poison later requests");
        try { await adapter.CompleteAsync(model, "fixture", request with { UserInstruction = "DETERMINISTIC_RATE_LIMIT" }, default); throw new InvalidOperationException("Rate limit fixture did not fire."); }
        catch (AiProviderExecutionException error) { Assert(error.Failure == AiProviderFailure.RateLimited, "Current-question fixture still applies"); }
    }
}

async Task VerifyInactiveWorkspace()
{
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AI:DeploymentDefault:Provider"] = "GEMINI",
        ["AI:Providers:GEMINI:ApiKey"] = "deployment-key-must-not-bypass-disable"
    }).Build();
    var workspace = new FakeWorkspace();
    var protection = new EphemeralDataProtectionProvider();
    foreach (var status in new[] { AiConfigurationValues.Disabled, AiConfigurationValues.Draft })
    {
        var store = new FakeStore(null) { State = new("workspace-a", status, "GEMINI", "gemini-2.5-flash", "WORKSPACE",
            true, false, null, null, null, false, false, 5, true, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, null, null) };
        var adapter = new ScriptedAdapter("GEMINI", []);
        var provider = new WorkspaceProductionAiProvider(new(workspace, store, protection, configuration, new(configuration)),
            [adapter], workspace, new FakeLedger(), new(TimeProvider.System), new(TimeProvider.System, 2, 100),
            new(TimeSpan.FromSeconds(2)), TimeProvider.System);
        try { await provider.CompleteAsync(request, default); throw new InvalidOperationException("Inactive Workspace executed AI."); }
        catch (AiProviderUnavailableException exception) { Assert(exception.Reason == "WORKSPACE_AI_INACTIVE", "Inactive reason is safe and explicit"); }
        Assert(adapter.Calls == 0 && store.LastWorkspaceId == "workspace-a", "Disable/draft blocks every provider attempt in trusted Workspace");
    }
    var defaultResolver = new WorkspaceAiProviderResolver(workspace, new FakeStore(null), protection, configuration, new(configuration));
    Assert((await defaultResolver.ResolveAsync(default)).Primary.Credential == "deployment-key-must-not-bypass-disable", "Only never-configured Workspaces retain deployment defaults");
}

async Task VerifyProactiveSchemas()
{
    const string output = """{"summary":"ok","suggestedNextStep":"review","taskDraft":{"title":"follow up","description":"review needs"}}""";
    foreach (var gemini in new[] { true, false })
    {
        var handler = new StubHandler(async (message, _) =>
        {
            using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync());
            var root = body.RootElement;
            var schema = gemini ? root.GetProperty("generationConfig").GetProperty("responseSchema")
                : root.GetProperty("text").GetProperty("format").GetProperty("schema");
            Assert(schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).SequenceEqual(["summary", "suggestedNextStep", "taskDraft"]), "Proactive top-level schema");
            var draft = schema.GetProperty("properties").GetProperty("taskDraft");
            Assert(schema.GetProperty("type").GetString() == (gemini ? "OBJECT" : "object")
                && draft.GetProperty("type").GetString() == (gemini ? "OBJECT" : "object")
                && draft.GetProperty("properties").GetProperty("title").GetProperty("type").GetString() == (gemini ? "STRING" : "string"),
                "Gemini REST enum casing does not alter OpenAI JSON Schema casing");
            Assert(draft.GetProperty("required").EnumerateArray().Select(x => x.GetString()).SequenceEqual(["title", "description"]), "Proactive text-only task schema");
            Assert(!schema.GetProperty("properties").TryGetProperty("why", out var whyProperty), "Provider has no Why authority");
            if (!gemini) Assert(!schema.GetProperty("additionalProperties").GetBoolean() && !draft.GetProperty("additionalProperties").GetBoolean(), "Strict OpenAI proactive schema");
            var envelope = gemini
                ? JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = output } } } } } })
                : JsonSerializer.Serialize(new { status = "completed", output = new[] { new { content = new[] { new { type = "output_text", text = output } } } } });
            return Json(HttpStatusCode.OK, envelope);
        });
        var http = new HttpClient(handler) { BaseAddress = new("https://example.invalid/") };
        IProductionAiProviderAdapter adapter = gemini ? new GeminiAiProvider(http, TimeProvider.System) : new OpenAiProvider(http, TimeProvider.System);
        var result = await adapter.CompleteAsync(gemini ? "gemini-2.5-flash" : "gpt-5-mini", "test-secret",
            request with { OutputContract = AiProviderOutputContract.ProactiveSuggestion }, default);
        Assert(ProactiveSuggestionOutputValidator.Validate(result.Content) is not null, "Proactive output traverses existing adapter");
    }
}

async Task VerifyProactivePlatform()
{
    var protection = new EphemeralDataProtectionProvider();
    var credential = protection.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1").Protect("proactive-test-secret");
    var workspace = new FakeWorkspace();
    foreach (var (name, model) in new[] { ("GEMINI", "gemini-2.5-flash"), ("OPENAI", "gpt-5-mini") })
    {
        var resolver = Resolver(new(new(name, model, "WORKSPACE", false, null, null, null, false), credential, null), workspace, protection);
        var adapter = new ScriptedAdapter(name, []);
        var ledger = new FakeLedger();
        IAiProvider provider = new WorkspaceProductionAiProvider(resolver, [adapter], workspace, ledger,
            new AiProviderCircuitBreaker(TimeProvider.System), new AiWorkspaceGuardrails(TimeProvider.System, 2, 100),
            new AiProviderRuntimeOptions(TimeSpan.FromSeconds(2)), TimeProvider.System);
        var execution = $"ai_exec_{Guid.NewGuid():N}";
        var result = await provider.CompleteAsync(request with { ExecutionId = execution, OutputContract = AiProviderOutputContract.ProactiveSuggestion }, default);
        Assert(result.Provider == name && result.Model == model && adapter.Calls == 1, "Proactive uses Workspace-selected production provider");
        Assert(ProactiveSuggestionOutputValidator.Validate(result.Content) is not null, "Proactive contract survives production stack");
        Assert(ledger.Attempts.Single().ExecutionId == execution, "Proactive provider attempt correlation");
    }
}

async Task VerifyGemini()
{
    var expectLowThinking = false;
    var handler = new StubHandler(async (message, _) =>
    {
        Assert(message.Headers.GetValues("x-goog-api-key").Single() == "gemini-secret", "Gemini auth header");
        Assert(!message.RequestUri!.ToString().Contains("gemini-secret", StringComparison.Ordinal), "Gemini secret absent from URL");
        using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync());
        var generation = body.RootElement.GetProperty("generationConfig");
        var schema = generation.GetProperty("responseSchema");
        Assert(schema.GetProperty("type").GetString() == "OBJECT"
            && schema.GetProperty("properties").GetProperty("attentionPoints").GetProperty("type").GetString() == "ARRAY"
            && schema.GetProperty("properties").GetProperty("attentionPoints").GetProperty("maxItems").GetString() == "5",
            "Gemini 2.5 and 3.8 use the canonical REST schema");
        if (expectLowThinking) Assert(generation.GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString() == "low", "Gemini 3.8 synthetic health check uses low thinking");
        else Assert(!generation.TryGetProperty("thinkingConfig", out var unusedThinkingConfig), "Chat and legacy model reasoning are not overridden");
        return Json(HttpStatusCode.OK, """{"responseId":"gem-req","candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"{\"summary\":\"ok\",\"suggestedNextAction\":null,\"attentionPoints\":[]}"}]}}],"usageMetadata":{"promptTokenCount":11,"candidatesTokenCount":7}}""");
    });
    var provider = new GeminiAiProvider(new HttpClient(handler) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System);
    var result = await provider.CompleteAsync("gemini-2.5-flash", "gemini-secret", request, CancellationToken.None);
    Assert(result.Provider == "GEMINI" && result.ProviderRequestId == "gem-req" && result.InputTokens == 11 && result.OutputTokens == 7, "Gemini result mapping");
    Assert((await provider.CompleteAsync("gemini-3.8-flash", "gemini-secret", request, CancellationToken.None)).Model == "gemini-3.8-flash", "Current Gemini model traverses the same structured adapter");
    expectLowThinking = true;
    await provider.CompleteAsync("gemini-3.8-flash", "gemini-secret", request with { IsConfigurationTest = true }, default);
    expectLowThinking = false;
    await provider.CompleteAsync("gemini-2.5-flash", "gemini-secret", request with { IsConfigurationTest = true }, default);
    await ExpectFailure(new GeminiAiProvider(new HttpClient(new StubHandler((_, _) => Json(HttpStatusCode.TooManyRequests, "{}"))) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System), AiProviderFailure.RateLimited);
    await ExpectFailure(new GeminiAiProvider(new HttpClient(new StubHandler((_, _) => Json(HttpStatusCode.OK, "{bad"))) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System), AiProviderFailure.InvalidResponse);
}

async Task VerifyGeminiRestContract()
{
    var expectedRequest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gemini-3.8-configuration-request.json")));
    var successEnvelope = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gemini-multipart-success.json"));
    var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
    var protection = new EphemeralDataProtectionProvider();
    var inMemoryCredential = Guid.NewGuid().ToString("N");
    var protectedValue = protection.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1").Protect(inMemoryCredential);
    var state = new WorkspaceAiConfigurationState("workspace-a", AiConfigurationValues.Draft, "GEMINI", "gemini-3.8-flash", "WORKSPACE",
        true, false, null, null, null, false, false, 7, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);
    var logger = new SafeTestLogger<AiConfigurationApplication>();
    AiConfigurationApplication App(FakeStore store, IProductionAiProviderAdapter adapter) => new(new FakeWorkspace(), new AllowAuthorizer(), store,
        new FakeLedger(), new(config), protection, config, [adapter], new(), new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)), TimeProvider.System, logger);
    var calls = 0;
    using var http = new HttpClient(new StubHandler(async (message, ct) =>
    {
        calls++;
        Assert(message.Method == HttpMethod.Post && message.RequestUri?.AbsoluteUri == "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent",
            "Gemini REST method, host, version, model and generateContent route match Google");
        Assert(message.Headers.GetValues("x-goog-api-key").Single() == inMemoryCredential && message.Headers.Authorization is null,
            "Gemini credential is only in the API key header");
        Assert(message.Content?.Headers.ContentType?.MediaType == "application/json", "Gemini request JSON content type");
        var actualRequest = JsonNode.Parse(await message.Content!.ReadAsStringAsync(ct));
        Assert(JsonNode.DeepEquals(expectedRequest, actualRequest), "Entire serialized synthetic request matches REST contract fixture, including casing, nullable, token budget and low thinking");
        return Json(HttpStatusCode.OK, successEnvelope);
    })) { BaseAddress = new("https://generativelanguage.googleapis.com/") };
    var store = new FakeStore(null) { State = state, ProtectedCredential = protectedValue };
    var app = App(store, new GeminiAiProvider(http, TimeProvider.System));
    var success = await app.TestAsync(7, "rest-contract", "rest-correlation", default);
    Assert(success.Value?.Configuration is { IsValidated: true, Version: 8 } && calls == 1, "Multipart JSON with thought part validates only after complete STOP");
    Assert((await app.ActivateAsync(7, "rest-stale", "rest-correlation", default)).Error?.Code == "AI_CONFIGURATION_VERSION_CONFLICT"
        && (await app.ActivateAsync(8, "rest-activate", "rest-correlation", default)).IsSuccess, "Mock Activate still requires latest validated version");

    const string valid = """{"summary":"ok","attentionPoints":[]}""";
    string Envelope(string text, string? reason = "STOP") => JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = reason, content = new { parts = new[] { new { text } } } } } });
    var failures = new (string Body, string? SafeCode)[]
    {
        (Envelope(valid, "MAX_TOKENS"), "MAX_TOKENS"), // Valid-looking JSON must still fail on truncation.
        (Envelope(valid, null), "FINISH_REASON_MISSING"),
        (Envelope(valid, "CONTINUATION"), "FINISH_REASON_UNSUPPORTED"),
        (Envelope(valid, "untrusted-provider-value"), "FINISH_REASON_UNSUPPORTED"),
        (Envelope("{\"summary\":"), null),
        (Envelope("{\"summary\":\"missing attention\"}"), null),
        (Envelope("{\"summary\":\"ok\",\"attentionPoints\":[],\"suggestedNextAction\":42}"), null),
        (Envelope("{\"summary\":\"ok\",\"attentionPoints\":[],\"unexpected\":true}"), null),
        ("""{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"{}"},{"text":42}]}}]}""", null),
        ("""{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"{}","thought":"true"}]}}]}""", null),
        ("""{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"{}","thought":true}]}}]}""", null),
        ("{malformed-envelope", null)
    };
    foreach (var item in failures)
    {
        foreach (var previouslyValidated in new[] { false, true })
        {
            var failedCalls = 0;
            var failedStore = new FakeStore(null) { State = state with { IsValidated = previouslyValidated }, ProtectedCredential = protectedValue };
            using var failedHttp = new HttpClient(new StubHandler((_, _) => { failedCalls++; return Json(HttpStatusCode.OK, item.Body); })) { BaseAddress = new("https://example.invalid/") };
            var failedApp = App(failedStore, new GeminiAiProvider(failedHttp, TimeProvider.System));
            var result = await failedApp.TestAsync(7, "rest-invalid", "rest-invalid-correlation", default);
            Assert(result.Error?.Code == "AI_PROVIDER_RESPONSE_INVALID" && failedStore.State?.IsValidated == false && failedCalls == 1,
                "Truncated, incomplete, malformed or contract-invalid output never validates, retries, or retains earlier validation");
            Assert((await failedApp.ActivateAsync(failedStore.State!.Version, "rest-invalid-activate", "rest-invalid-correlation", default)).Error?.Code == "AI_CONFIGURATION_NOT_VALIDATED",
                "Every invalid-output fixture is denied Activate");
            if (item.SafeCode is not null) Assert(logger.Messages.Any(x => x.Contains($"ProviderErrorCode={item.SafeCode}", StringComparison.Ordinal)
                && x.Contains("ProviderHttpStatus=200", StringComparison.Ordinal)), "Only allowlisted finish diagnostics accompany HTTP success");
        }
    }
    Assert(logger.Messages.All(x => !x.Contains(inMemoryCredential, StringComparison.Ordinal) && !x.Contains(protectedValue, StringComparison.Ordinal)
        && !x.Contains("untrusted-provider-value", StringComparison.Ordinal)), "REST fixture diagnostics contain neither credentials nor raw finish reason");
    var validator = new AiProviderOutputValidator();
    foreach (var text in new[] { valid, """{"summary":"ok","attentionPoints":[],"suggestedNextAction":null}""", """{"summary":"ok","attentionPoints":[],"suggestedNextAction":"review"}""" })
        Assert(validator.Validate(text) is not null, "Optional nullable schema matches output validator: omitted/null/string");

    foreach (var reason in new[] { "SAFETY", "PROHIBITED_CONTENT", "RECITATION", "SPII" })
        await ExpectFailure(new GeminiAiProvider(new HttpClient(new StubHandler((_, _) => Json(HttpStatusCode.OK, Envelope(valid, reason)))) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System), AiProviderFailure.SafetyRefusal);
    await ExpectFailure(new GeminiAiProvider(new HttpClient(new StubHandler((_, _) => Json(HttpStatusCode.OK, """{"promptFeedback":{"blockReason":"SAFETY"}}"""))) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System), AiProviderFailure.SafetyRefusal);

    // Verify the adapter's own transport classification in both Chat and Test, without a network connection.
    foreach (var isTest in new[] { false, true })
    {
        using var timeoutHttp = new HttpClient(new StubHandler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Json(HttpStatusCode.OK, "{}"); }))
            { BaseAddress = new("https://example.invalid/"), Timeout = TimeSpan.FromMilliseconds(25) };
        try { await new GeminiAiProvider(timeoutHttp, TimeProvider.System).CompleteAsync("gemini-3.8-flash", inMemoryCredential, request with { IsConfigurationTest = isTest }, default); throw new InvalidOperationException("Expected HTTP timeout."); }
        catch (AiProviderExecutionException error) { Assert(error.Failure == AiProviderFailure.Timeout && error.TimeoutSource == AiProviderTimeoutSource.HttpClientDeadline && error.ProviderHttpStatus is null, "Transport timeout has evidence, without inventing Google HTTP status"); }
    }
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var caller = new CancellationTokenSource();
    using var callerHttp = new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BlockingReadStream(entered)) }))
        { BaseAddress = new("https://example.invalid/") };
    var pending = new GeminiAiProvider(callerHttp, TimeProvider.System).CompleteAsync("gemini-3.8-flash", inMemoryCredential, request with { IsConfigurationTest = true }, caller.Token);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    caller.Cancel();
    try { await pending; throw new InvalidOperationException("Expected caller cancellation."); }
    catch (OperationCanceledException) when (caller.IsCancellationRequested) { }
    Console.WriteLine("GEMINI_REST_CONTRACT_MOCK_PASS (request, multipart, finish reason, validation, cancellation)");
}

void VerifyConfigurationTimeoutOptions()
{
    var cases = new (int? Provider, int? Test, int ExpectedProvider, int ExpectedTest)[]
    { (null, null, 10, 30), (10, 30, 10, 30), (50, null, 50, 50), (10, 0, 10, 1), (10, 90, 10, 60), (90, null, 60, 60) };
    foreach (var item in cases)
    {
        var values = new Dictionary<string, string?>();
        if (item.Provider is { } provider) values["AI:Provider:TimeoutSeconds"] = provider.ToString();
        if (item.Test is { } test) values["AI:Provider:ConfigurationTestTimeoutSeconds"] = test.ToString();
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddProvidersModule(config, new TestHostEnvironment());
        var options = (AiProviderRuntimeOptions)services.Single(x => x.ServiceType == typeof(AiProviderRuntimeOptions)).ImplementationInstance!;
        Assert(options.Timeout.TotalSeconds == item.ExpectedProvider && options.TestTimeoutFor("GEMINI").TotalSeconds == item.ExpectedTest
            && options.TestTimeoutFor("OPENAI").TotalSeconds == item.ExpectedProvider,
            "Gemini Test timeout is independently bounded and preserves OpenAI/Chat timeout");
        services.AddLogging();
        using var scope = services.BuildServiceProvider();
        using var geminiHttp = scope.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GeminiAiProvider));
        Assert(geminiHttp.BaseAddress?.Host == "generativelanguage.googleapis.com" && geminiHttp.Timeout == TimeSpan.FromSeconds(100)
            && geminiHttp.Timeout > options.TestTimeoutFor("GEMINI"), "Finite HttpClient deadline does not preempt the application cap");
    }
}

async Task VerifyControlledConfigurationDeadlines()
{
    const string envelope = """{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"{\"summary\":\"ok\",\"attentionPoints\":[]}"}]}}]}""";
    var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
    var protection = new EphemeralDataProtectionProvider();
    var protectedValue = protection.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1").Protect("controlled-fixture-credential");
    foreach (var scenario in new[] { "success-after-chat-budget", "no-response", "body-stalled", "transport-ignores-cancellation" })
    {
        var clock = new ManualDeadlineClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appLogger = new SafeTestLogger<AiConfigurationApplication>();
        var adapterLogger = new SafeTestLogger<GeminiAiProvider>();
        var calls = 0;
        var store = new FakeStore(null) { ProtectedCredential = protectedValue,
            State = new("workspace-a", AiConfigurationValues.Draft, "GEMINI", "gemini-3.8-flash", "WORKSPACE", true,
                false, null, null, null, false, false, 7, false, clock.GetUtcNow(), clock.GetUtcNow(), null, null) };
        using var http = new HttpClient(new StubHandler(async (_, ct) =>
        {
            calls++;
            if (scenario == "body-stalled") return new(HttpStatusCode.OK) { Content = new StreamContent(new BlockingReadStream(entered)) };
            entered.TrySetResult();
            if (scenario == "no-response") await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            else if (scenario == "transport-ignores-cancellation") await release.Task;
            else await release.Task.WaitAsync(ct);
            return Json(HttpStatusCode.OK, envelope);
        })) { BaseAddress = new("https://example.invalid/") };
        var adapter = new ObservedAdapter(new GeminiAiProvider(http, clock, adapterLogger));
        var app = new AiConfigurationApplication(new FakeWorkspace(), new AllowAuthorizer(), store, new FakeLedger(), new(config),
            protection, config, [adapter], new(), new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)), clock, appLogger);
        var pending = app.TestAsync(7, "controlled-test", "controlled-correlation", default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); // Guard a broken fixture; no provider/network wait.
        clock.Advance(TimeSpan.FromSeconds(scenario == "success-after-chat-budget" ? 11 : 30));
        if (scenario == "success-after-chat-budget") release.TrySetResult();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        // Release any deliberately non-cooperative fixture after proving the caller has returned.
        release.TrySetResult();
        try { await adapter.Pending!.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { }
        catch (AiProviderExecutionException) { }
        Assert(calls == 1, "Synthetic Test never retries transport");
        if (scenario == "success-after-chat-budget")
        {
            Assert(result.Value?.Configuration is { IsValidated: true, Version: 8 }, "Valid response after 10s but before 30s succeeds");
            Assert((await app.ActivateAsync(8, "controlled-activate", "controlled-correlation", default)).IsSuccess, "Successful Test allows policy-gated Activate");
        }
        else
        {
            Assert(result.Error?.Code == "AI_PROVIDER_TIMEOUT" && store.ValidationWrites == 0 && store.State is { IsValidated: false, Version: 7 }, "No headers/stalled body hits the finite application deadline");
            Assert((await app.ActivateAsync(7, "controlled-activate", "controlled-correlation", default)).Error?.Code == "AI_CONFIGURATION_NOT_VALIDATED", "Timeout cannot activate");
            Assert(appLogger.Messages.Any(x => x.Contains("ElapsedMs=30000", StringComparison.Ordinal) && x.Contains("TimeoutSource=ApplicationDeadline", StringComparison.Ordinal)), "Fake-clock deadline is measured exactly");
            if (scenario != "transport-ignores-cancellation") Assert(adapterLogger.Messages.Any(x => x.Contains(scenario == "body-stalled" ? "Phase=READ_RESPONSE ProviderHttpStatus=200" : "Phase=SEND_OR_WAIT_HEADERS ProviderHttpStatus=", StringComparison.Ordinal)), "Transport phase distinguishes missing headers from an unfinished response body");
        }
        Assert(store.ProtectedCredential == protectedValue && appLogger.Messages.Concat(adapterLogger.Messages).All(x => !x.Contains("controlled-fixture-credential", StringComparison.Ordinal)
            && !x.Contains(protectedValue, StringComparison.Ordinal)), "Timeout diagnostics and state never leak or rewrite credentials");
        Assert(clock.TimerCount == 0, "Configuration deadline timers are disposed");
    }
    foreach (var (status, code, reason) in new[]
    {
        (HttpStatusCode.Unauthorized, "AI_PROVIDER_UNAVAILABLE", "CREDENTIAL_REJECTED"),
        (HttpStatusCode.Forbidden, "AI_PROVIDER_UNAVAILABLE", "PROVIDER_PERMISSION_DENIED"),
        (HttpStatusCode.TooManyRequests, "AI_PROVIDER_RATE_LIMITED", (string?)null),
        (HttpStatusCode.InternalServerError, "AI_PROVIDER_UNAVAILABLE", null),
        (HttpStatusCode.ServiceUnavailable, "AI_PROVIDER_UNAVAILABLE", null),
        (HttpStatusCode.GatewayTimeout, "AI_PROVIDER_TIMEOUT", null)
    })
    {
        var bodyRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeStore(null) { ProtectedCredential = protectedValue,
            State = new("workspace-a", AiConfigurationValues.Draft, "GEMINI", "gemini-3.8-flash", "WORKSPACE", true,
                false, null, null, null, false, false, 7, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null) };
        var adapter = new GeminiAiProvider(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(status)
            { Content = new StreamContent(new BlockingReadStream(bodyRead)) })) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System);
        var app = new AiConfigurationApplication(new FakeWorkspace(), new AllowAuthorizer(), store, new FakeLedger(), new(config),
            protection, config, [adapter], new(), new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)), TimeProvider.System, new SafeTestLogger<AiConfigurationApplication>());
        var result = await app.TestAsync(7, "error-headers", "error-correlation", default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert(result.Error?.Code == code && result.Error?.FieldErrors?.GetValueOrDefault("provider")?.Single() == reason
            && !bodyRead.Task.IsCompleted && store.ValidationWrites == 0, "Conclusive HTTP error headers are never hidden by a stalled body timeout");
    }
}

async Task VerifyGeminiFailures()
{
    var cases = new (HttpStatusCode Status, string Body, AiProviderFailure Failure)[]
    {
        (HttpStatusCode.BadRequest, """{"error":{"status":"INVALID_ARGUMENT","message":"sensitive-value","details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"API_KEY_INVALID","metadata":{"key":"sensitive-value"}}]}}""", AiProviderFailure.AuthenticationFailure),
        (HttpStatusCode.BadRequest, """{"error":{"details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"API_KEY_EXPIRED"}]}}""", AiProviderFailure.AuthenticationFailure),
        (HttpStatusCode.BadRequest, """{"error":{"status":"INVALID_ARGUMENT","message":"sensitive-value"}}""", AiProviderFailure.InvalidRequest),
        (HttpStatusCode.BadRequest, """{"error":{"code":400,"status":"INVALID_ARGUMENT","message":"Invalid generationConfig.responseSchema.type"}}""", AiProviderFailure.InvalidRequest),
        (HttpStatusCode.BadRequest, """{"error":{"code":400,"status":"INVALID_ARGUMENT","message":"Model does not support generateContent"}}""", AiProviderFailure.InvalidRequest),
        (HttpStatusCode.BadRequest, """{"error":{"status":"FAILED_PRECONDITION"}}""", AiProviderFailure.BillingRequired),
        (HttpStatusCode.PaymentRequired, "{}", AiProviderFailure.BillingRequired),
        (HttpStatusCode.Unauthorized, "{}", AiProviderFailure.AuthenticationFailure),
        (HttpStatusCode.Forbidden, """{"error":{"status":"PERMISSION_DENIED"}}""", AiProviderFailure.PermissionDenied),
        (HttpStatusCode.NotFound, "{}", AiProviderFailure.ModelUnavailable),
        (HttpStatusCode.TooManyRequests, """{"error":{"status":"RESOURCE_EXHAUSTED"}}""", AiProviderFailure.RateLimited),
        (HttpStatusCode.GatewayTimeout, "{}", AiProviderFailure.Timeout),
        (HttpStatusCode.ServiceUnavailable, "{}", AiProviderFailure.Unavailable),
        (HttpStatusCode.InternalServerError, "{}", AiProviderFailure.Unavailable),
        (HttpStatusCode.BadRequest, "not-json-sensitive-value", AiProviderFailure.InvalidRequest),
        (HttpStatusCode.BadRequest, "[]", AiProviderFailure.InvalidRequest),
        (HttpStatusCode.BadRequest, """{"error":{"details":[{"reason":"API_KEY_INVALID"}]}}""", AiProviderFailure.InvalidRequest),
        (HttpStatusCode.BadRequest, new string('x', 20000), AiProviderFailure.InvalidRequest)
    };
    foreach (var item in cases)
    {
        var calls = 0;
        var handler = new StubHandler((_, _) => { calls++; return Json(item.Status, item.Body); });
        var adapter = new GeminiAiProvider(new HttpClient(handler) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System);
        try { await adapter.CompleteAsync("gemini-2.5-flash", "fixture", request, default); throw new InvalidOperationException("Expected provider failure."); }
        catch (AiProviderExecutionException error)
        {
            Assert(error.Failure == item.Failure && error.ProviderHttpStatus == (int)item.Status, "Gemini HTTP status and safe classification survive");
            Assert(!error.ToString().Contains("sensitive-value", StringComparison.Ordinal), "Provider body is never retained in exceptions");
            Assert(calls == 1, "Configuration adapter does not retry failed requests");
        }
    }
    foreach (var malformed in new[] { "[]", """{"candidates":[null]}""", """{"candidates":[{"finishReason":"STOP","content":{"parts":{}}}]}""", """{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":42}]}}]}""" })
        await ExpectFailure(new GeminiAiProvider(new HttpClient(new StubHandler((_, _) => Json(HttpStatusCode.OK, malformed))) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System), AiProviderFailure.InvalidResponse);
}

async Task VerifyConfigurationFailures()
{
    var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
    var catalog = new AiProviderCatalog(config);
    Assert(catalog.IsAdmitted("GEMINI", "gemini-3.8-flash") && catalog.IsAdmitted("GEMINI", "gemini-2.5-flash")
        && !catalog.IsAdmitted("GEMINI", "unlisted-model"), "Current Gemini model is admitted while legacy access and allowlist remain supported");
    var protection = new EphemeralDataProtectionProvider();
    var protectedValue = protection.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1").Protect("configuration-fixture-secret");
    var state = new WorkspaceAiConfigurationState("workspace-a", AiConfigurationValues.Draft, "GEMINI", "gemini-2.5-flash", "WORKSPACE",
        true, false, null, null, null, false, false, 7, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);
    var logger = new SafeTestLogger<AiConfigurationApplication>();
    AiConfigurationApplication App(FakeStore store, IProductionAiProviderAdapter adapter, AiProviderRuntimeOptions? options = null) => new(new FakeWorkspace(), new AllowAuthorizer(),
        store, new FakeLedger(), new(config), protection, config, [adapter], new(), options ?? new(TimeSpan.FromMilliseconds(100)), TimeProvider.System, logger);
    foreach (var (value, expected) in new[] { ((string?)null, "CREDENTIAL_MISSING"), ("invalid-protected-payload", "CREDENTIAL_UNREADABLE") })
    {
        var adapter = new ScriptedAdapter("GEMINI", []);
        var store = new FakeStore(null) { State = state, ProtectedCredential = value };
        var result = await App(store, adapter).TestAsync(7, "test-key", "test-correlation", default);
        Assert(result.Error?.FieldErrors?["provider"].Single() == expected && adapter.Calls == 0, "Missing/unreadable credentials fail before provider call");
    }
    foreach (var (failure, code, reason) in new[]
    {
        (AiProviderFailure.AuthenticationFailure, "AI_PROVIDER_UNAVAILABLE", "CREDENTIAL_REJECTED"),
        (AiProviderFailure.PermissionDenied, "AI_PROVIDER_UNAVAILABLE", "PROVIDER_PERMISSION_DENIED"),
        (AiProviderFailure.ModelUnavailable, "AI_PROVIDER_UNAVAILABLE", "MODEL_UNAVAILABLE"),
        (AiProviderFailure.BillingRequired, "AI_PROVIDER_UNAVAILABLE", "PROVIDER_ACCOUNT_NOT_READY"),
        (AiProviderFailure.InvalidRequest, "AI_PROVIDER_UNAVAILABLE", "PROVIDER_REQUEST_REJECTED"),
        (AiProviderFailure.NetworkFailure, "AI_PROVIDER_UNAVAILABLE", "PROVIDER_NETWORK_FAILURE"),
        (AiProviderFailure.RateLimited, "AI_PROVIDER_RATE_LIMITED", (string?)null),
        (AiProviderFailure.Timeout, "AI_PROVIDER_TIMEOUT", null),
        (AiProviderFailure.InvalidResponse, "AI_PROVIDER_RESPONSE_INVALID", null),
        (AiProviderFailure.SafetyRefusal, "AI_PROVIDER_SAFETY_REFUSAL", null)
    })
    {
        var store = new FakeStore(null) { State = state, ProtectedCredential = protectedValue };
        var adapter = new ScriptedAdapter("GEMINI", [failure]);
        var app = App(store, adapter);
        var result = await app.TestAsync(7, "test-key", "test-correlation", default);
        Assert(result.Error?.Code == code && result.Error?.FieldErrors?.GetValueOrDefault("provider")?.Single() == reason,
            "Configuration test preserves failure distinctions through application boundary");
        Assert(adapter.Calls == 1 && store.ValidationWrites == 0, "Failed test never validates or retries");
        Assert((await app.ActivateAsync(7, "activate-key", "test-correlation", default)).Error?.Code == "AI_CONFIGURATION_NOT_VALIDATED", "Failed test cannot activate");
        Assert(adapter.Requests.Single().ContextData == "{}" && adapter.Requests.Single().ContextCount == 0, "Configuration test sends only synthetic context");
        Assert(adapter.Requests.Single().IsConfigurationTest && adapter.Requests.Single().CorrelationId == "test-correlation", "Test purpose and safe correlation reach the adapter");
    }
    var successStore = new FakeStore(null) { State = state, ProtectedCredential = protectedValue };
    var successAdapter = new ScriptedAdapter("GEMINI", []);
    var successApp = App(successStore, successAdapter);
    Assert((await successApp.TestAsync(6, "stale-key", "test-correlation", default)).Error?.Code == "AI_CONFIGURATION_VERSION_CONFLICT"
        && successAdapter.Calls == 0, "Stale version fails before provider call");
    var success = await successApp.TestAsync(7, "test-key", "test-correlation", default);
    Assert(success.Value?.Configuration is { IsValidated: true, Version: 8 } && successStore.ValidationWrites == 1, "Only valid structured output validates current configuration");
    Assert((await successApp.ActivateAsync(7, "stale-key", "test-correlation", default)).Error?.Code == "AI_CONFIGURATION_VERSION_CONFLICT", "Activation requires version returned by Test");
    Assert((await successApp.ActivateAsync(8, "activate-key", "test-correlation", default)).IsSuccess, "Validated fixture activates with current version");
    var timeoutStore = new FakeStore(null) { State = state, ProtectedCredential = protectedValue };
    var timeoutAdapter = new BlockingAdapter("GEMINI");
    Assert((await App(timeoutStore, timeoutAdapter).TestAsync(7, "timeout-key", "test-correlation", default)).Error?.Code == "AI_PROVIDER_TIMEOUT"
        && timeoutAdapter.Calls == 1 && timeoutStore.ValidationWrites == 0, "Real cancellation deadline fails without validation or retries");
    Assert(logger.Messages.Any(x => x.Contains("TimeoutSource=ApplicationDeadline", StringComparison.Ordinal) && x.Contains("ElapsedMs=", StringComparison.Ordinal)), "Application deadline is timed and classified");
    using (var caller = new CancellationTokenSource())
    {
        var cancellation = App(timeoutStore, new BlockingAdapter("GEMINI")).TestAsync(7, "cancel-key", "caller-correlation", caller.Token);
        caller.Cancel();
        try { await cancellation; throw new InvalidOperationException("Expected caller cancellation."); }
        catch (OperationCanceledException) when (caller.IsCancellationRequested) { }
    }
    Assert(logger.Messages.Any(x => x.Contains("caller cancelled", StringComparison.Ordinal) && x.Contains("caller-correlation", StringComparison.Ordinal)), "Caller cancellation is not labelled a provider timeout");
    using (var preCancelled = new CancellationTokenSource())
    {
        preCancelled.Cancel();
        var skippedAdapter = new ScriptedAdapter("GEMINI", []);
        try { await App(timeoutStore, skippedAdapter).TestAsync(7, "pre-cancel-key", "pre-cancel-correlation", preCancelled.Token); throw new InvalidOperationException("Expected pre-cancelled request."); }
        catch (OperationCanceledException) when (preCancelled.IsCancellationRequested) { }
        Assert(skippedAdapter.Calls == 0, "Pre-cancelled requests do not start a paid provider call");
    }
    var clientTimeoutAdapter = new GeminiAiProvider(new HttpClient(new StubHandler(async (_, ct) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        return Json(HttpStatusCode.OK, "{}");
    })) { BaseAddress = new("https://example.invalid/"), Timeout = TimeSpan.FromMilliseconds(25) }, TimeProvider.System);
    Assert((await App(timeoutStore, clientTimeoutAdapter, new(TimeSpan.FromSeconds(1))).TestAsync(7, "client-timeout", "client-correlation", default)).Error?.Code == "AI_PROVIDER_TIMEOUT",
        "HttpClient deadline remains a provider timeout");
    Assert(logger.Messages.Any(x => x.Contains("client-correlation", StringComparison.Ordinal) && x.Contains("TimeoutSource=HttpClientDeadline", StringComparison.Ordinal)), "HttpClient deadline is distinguished from application deadline");
    var providerTimeoutAdapter = new GeminiAiProvider(new HttpClient(new StubHandler((_, _) => Json(HttpStatusCode.GatewayTimeout, """{"error":{"status":"DEADLINE_EXCEEDED"}}""")))
        { BaseAddress = new("https://example.invalid/") }, TimeProvider.System);
    Assert((await App(timeoutStore, providerTimeoutAdapter).TestAsync(7, "provider-timeout", "provider-correlation", default)).Error?.Code == "AI_PROVIDER_TIMEOUT", "Provider HTTP 504 is preserved");
    Assert(logger.Messages.Any(x => x.Contains("provider-correlation", StringComparison.Ordinal) && x.Contains("ProviderHttpStatus=504", StringComparison.Ordinal)
        && x.Contains("TimeoutSource=ProviderHttpDeadline", StringComparison.Ordinal)), "Google HTTP deadline is distinguishable");
    var unclassifiedAdapter = new GeminiAiProvider(new HttpClient(new StubHandler((_, _) => Task.FromException<HttpResponseMessage>(new OperationCanceledException())))
        { BaseAddress = new("https://example.invalid/") }, TimeProvider.System);
    Assert((await App(timeoutStore, unclassifiedAdapter).TestAsync(7, "unclassified-cancel", "unclassified-correlation", default)).Error?.FieldErrors?["provider"].Single() == "PROVIDER_REQUEST_CANCELLED",
        "Cancellation without timeout evidence is never mislabeled as provider timeout");
    var validatedStore = new FakeStore(null) { State = state with { IsValidated = true }, ProtectedCredential = protectedValue };
    var retestApp = App(validatedStore, new ScriptedAdapter("GEMINI", [AiProviderFailure.NetworkFailure]));
    Assert(!(await retestApp.TestAsync(7, "retest-key", "retest-correlation", default)).IsSuccess
        && validatedStore.State is { IsValidated: false, Version: 8 } && validatedStore.ProtectedCredential == protectedValue,
        "Failed re-test revokes prior validation without changing the credential");
    Assert((await retestApp.ActivateAsync(8, "retest-activate", "retest-correlation", default)).Error?.Code == "AI_CONFIGURATION_NOT_VALIDATED",
        "Prior successful validation cannot activate after a failed re-test");
    var concurrentStore = new FakeStore(null) { State = state with { IsValidated = true }, ProtectedCredential = protectedValue };
    var concurrentAdapter = new GeminiAiProvider(new HttpClient(new StubHandler((_, _) =>
    {
        concurrentStore.State = state with { Version = 8, IsValidated = true };
        return Json(HttpStatusCode.ServiceUnavailable, "{}");
    })) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System);
    Assert((await App(concurrentStore, concurrentAdapter).TestAsync(7, "concurrent-failure", "concurrent-correlation", default)).Error?.Code == "AI_CONFIGURATION_VERSION_CONFLICT"
        && concurrentStore.State is { Version: 8, IsValidated: true } && concurrentStore.ValidationWrites == 0,
        "Failed re-test cannot revoke validation for a newer concurrent configuration");
    foreach (var networkError in new[] { HttpRequestError.NameResolutionError, HttpRequestError.SecureConnectionError, HttpRequestError.ConnectionError })
    {
        var networkAdapter = new GeminiAiProvider(new HttpClient(new StubHandler((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException(networkError, "sensitive-network-diagnostic"))))
            { BaseAddress = new("https://example.invalid/") }, TimeProvider.System);
        Assert((await App(timeoutStore, networkAdapter).TestAsync(7, "network-key", "network-correlation", default)).Error?.FieldErrors?["provider"].Single() == "PROVIDER_NETWORK_FAILURE", "DNS/TLS/network failures remain distinct from deadline errors");
        Assert(logger.Messages.Any(x => x.Contains($"NetworkError={networkError}", StringComparison.Ordinal)) && logger.Messages.All(x => !x.Contains("sensitive-network-diagnostic", StringComparison.Ordinal)), "Only the safe network error enum is logged");
    }
    var delayedStore = new FakeStore(null) { State = state, ProtectedCredential = protectedValue };
    var delayedAdapter = new GeminiAiProvider(new HttpClient(new StubHandler(async (_, ct) =>
    {
        await Task.Delay(40, ct);
        return Json(HttpStatusCode.OK, """{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"{\"summary\":\"ok\",\"attentionPoints\":[]}"}]}}]}""");
    })) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System);
    Assert((await App(delayedStore, delayedAdapter, new(TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(1))).TestAsync(7, "test-budget", "budget-correlation", default)).IsSuccess,
        "Synthetic Test uses its own budget rather than the shorter Chat timeout");
    var raceStore = new FakeStore(null) { State = state, ProtectedCredential = protectedValue };
    var raceAdapter = new GeminiAiProvider(new HttpClient(new StubHandler((_, _) =>
    {
        raceStore.State = state with { Version = 8 };
        return Json(HttpStatusCode.OK, """{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"{\"summary\":\"ok\",\"attentionPoints\":[]}"}]}}]}""");
    })) { BaseAddress = new("https://example.invalid/") }, TimeProvider.System);
    Assert((await App(raceStore, raceAdapter).TestAsync(7, "race-key", "test-correlation", default)).Error?.Code == "AI_CONFIGURATION_VERSION_CONFLICT"
        && raceStore.ValidationWrites == 0, "Concurrent configuration change cannot be validated by an older provider response");
    var httpStore = new FakeStore(null) { State = state, ProtectedCredential = protectedValue };
    var badKeyAdapter = new GeminiAiProvider(new HttpClient(new StubHandler((_, _) => Json(HttpStatusCode.BadRequest,
        """{"error":{"status":"INVALID_ARGUMENT","message":"sensitive-provider-body","details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"API_KEY_INVALID"}]}}""")))
        { BaseAddress = new("https://example.invalid/") }, TimeProvider.System);
    Assert((await App(httpStore, badKeyAdapter).TestAsync(7, "http-key", "http-correlation", default)).Error?.FieldErrors?["provider"].Single() == "CREDENTIAL_REJECTED",
        "Gemini ErrorInfo reaches configuration boundary without a generic outage");
    Assert(logger.Messages.Any(x => x.Contains("http-correlation", StringComparison.Ordinal) && x.Contains("ProviderHttpStatus=400", StringComparison.Ordinal)
        && x.Contains("ProviderErrorCode=API_KEY_INVALID", StringComparison.Ordinal)), "Safe provider status/code can be correlated without DevTools");
    var malformedAdapter = new GeminiAiProvider(new HttpClient(new StubHandler((_, _) => Json(HttpStatusCode.OK,
        """{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"{\"summary\":\"missing attention points\"}"}]}}]}""")))
        { BaseAddress = new("https://example.invalid/") }, TimeProvider.System);
    Assert((await App(httpStore, malformedAdapter).TestAsync(7, "invalid-key", "http-correlation", default)).Error?.Code == "AI_PROVIDER_RESPONSE_INVALID"
        && httpStore.ValidationWrites == 0, "HTTP 200 without the structured contract never validates");
    Assert(AiErrors.ProviderUnavailable("PROVIDER_NETWORK_FAILURE").Retryable && !AiErrors.ProviderUnavailable("PROVIDER_REQUEST_REJECTED").Retryable,
        "Safe reasons preserve transient versus terminal retry guidance");
    Assert(logger.Messages.Any(x => x.Contains("test-correlation", StringComparison.Ordinal))
        && logger.Messages.All(x => !x.Contains("configuration-fixture-secret", StringComparison.Ordinal) && !x.Contains(protectedValue, StringComparison.Ordinal)
            && !x.Contains("sensitive-provider-body", StringComparison.Ordinal)), "Diagnostics correlate without credential leakage");
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
    using (var admission = breaker.Admit(scope)) { Assert(admission.IsAdmitted, "Circuit begins closed"); admission.TransientFailure(); }
    using (var admission = breaker.Admit(scope)) { Assert(admission.IsAdmitted, "Circuit stays closed below threshold"); admission.TransientFailure(); }
    using (var admission = breaker.Admit(scope)) { Assert(admission.IsAdmitted, "Circuit permits threshold attempt"); admission.TransientFailure(); }
    using (var admission = breaker.Admit(scope)) Assert(!admission.IsAdmitted, "Circuit opens after repeated transient failures");
    await Task.Delay(10);
    using (var probe = breaker.Admit(scope))
    {
        Assert(probe.IsAdmitted, "Circuit admits one half-open probe");
        using var blocked = breaker.Admit(scope); Assert(!blocked.IsAdmitted, "Circuit permits only one half-open probe");
        probe.Success();
    }
    using (var admission = breaker.Admit(scope)) Assert(admission.IsAdmitted, "Successful probe closes circuit");

    var protection = new EphemeralDataProtectionProvider();
    var protector = protection.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1");
    var active = new ActiveWorkspaceAiPolicy(new("GEMINI", "gemini-2.5-flash", "WORKSPACE", true, "OPENAI", "gpt-5-mini", "WORKSPACE", false),
        protector.Protect("primary"), protector.Protect("fallback"));
    var primaryOnly = new ActiveWorkspaceAiPolicy(new("GEMINI", "gemini-2.5-flash", "WORKSPACE", false, null, null, null, false), protector.Protect("primary"), null);
    var workspace = new FakeWorkspace(); var scopedBreaker = new AiProviderCircuitBreaker(TimeProvider.System, 3, TimeSpan.FromMinutes(1));
    var primaryScope = "workspace-a:GEMINI:gemini-2.5-flash";
    for (var index = 0; index < 3; index++) { using var admission = scopedBreaker.Admit(primaryScope); admission.TransientFailure(); }
    var skippedPrimary = new ScriptedAdapter("GEMINI", []); var successfulFallback = new ScriptedAdapter("OPENAI", []); var ledger = new FakeLedger();
    var result = await Provider(Resolver(active, workspace, protection), workspace, ledger, skippedPrimary, successfulFallback, breaker: scopedBreaker)
        .CompleteAsync(request with { ExecutionId = "execution-circuit-open" }, CancellationToken.None);
    Assert(result.Provider == "OPENAI" && skippedPrimary.Calls == 0 && successfulFallback.Calls == 1, "OPEN circuit skips primary transport and uses fallback");
    Assert(ledger.Attempts.Select(item => item.Status).SequenceEqual(["CIRCUIT_OPEN", "SUCCEEDED"]), "Circuit-open skip is auditable");

    async Task<AiProviderCircuitBreaker> HalfOpenBreaker()
    {
        var value = new AiProviderCircuitBreaker(TimeProvider.System, 1, TimeSpan.FromMilliseconds(10));
        using (var admission = value.Admit(primaryScope)) admission.TransientFailure();
        await Task.Delay(20);
        return value;
    }
    async Task AssertLaterProbe(AiProviderCircuitBreaker value, string name)
    {
        using (var blocked = value.Admit(primaryScope)) Assert(!blocked.IsAdmitted, $"{name} reopens for cooldown");
        await Task.Delay(20);
        using var later = value.Admit(primaryScope); Assert(later.IsAdmitted, $"{name} releases the half-open probe for a later request"); later.Release();
    }

    var successBreaker = await HalfOpenBreaker();
    var successResult = await Provider(Resolver(primaryOnly, workspace, protection), workspace, new FakeLedger(),
        new ScriptedAdapter("GEMINI", []), new ScriptedAdapter("OPENAI", []), breaker: successBreaker)
        .CompleteAsync(request with { ExecutionId = "execution-half-open-success" }, CancellationToken.None);
    Assert(successResult.Provider == "GEMINI", "HALF_OPEN success completes through WorkspaceProductionAiProvider");
    using (var closed = successBreaker.Admit(primaryScope)) Assert(closed.IsAdmitted, "HALF_OPEN success closes the circuit");

    foreach (var terminal in new[] { AiProviderFailure.SafetyRefusal, AiProviderFailure.AuthenticationFailure })
    {
        var terminalBreaker = await HalfOpenBreaker();
        try
        {
            await Provider(Resolver(primaryOnly, workspace, protection), workspace, new FakeLedger(),
                new ScriptedAdapter("GEMINI", [terminal]), new ScriptedAdapter("OPENAI", []), breaker: terminalBreaker)
                .CompleteAsync(request with { ExecutionId = $"execution-half-open-{terminal}" }, CancellationToken.None);
        }
        catch (Exception exception) when (exception is AiProviderSafetyRefusalException or AiProviderUnavailableException) { }
        await AssertLaterProbe(terminalBreaker, $"HALF_OPEN {terminal}");
    }

    var cancellationBreaker = await HalfOpenBreaker();
    using (var callerCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25)))
    {
        try
        {
            await Provider(Resolver(primaryOnly, workspace, protection), workspace, new FakeLedger(),
                new BlockingAdapter("GEMINI"), new ScriptedAdapter("OPENAI", []), TimeSpan.FromSeconds(2), cancellationBreaker)
                .CompleteAsync(request with { ExecutionId = "execution-half-open-cancel" }, callerCancellation.Token);
        }
        catch (OperationCanceledException) when (callerCancellation.IsCancellationRequested) { }
    }
    await AssertLaterProbe(cancellationBreaker, "HALF_OPEN caller cancellation");

    var timeoutBreaker = await HalfOpenBreaker();
    try
    {
        await Provider(Resolver(primaryOnly, workspace, protection), workspace, new FakeLedger(),
            new BlockingAdapter("GEMINI"), new ScriptedAdapter("OPENAI", []), TimeSpan.FromMilliseconds(25), timeoutBreaker)
            .CompleteAsync(request with { ExecutionId = "execution-half-open-timeout" }, CancellationToken.None);
    }
    catch (AiProviderUnavailableException) { }
    using (var reopened = timeoutBreaker.Admit(primaryScope)) Assert(!reopened.IsAdmitted, "HALF_OPEN transient timeout reopens the circuit");
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
sealed class ObservedAdapter(IProductionAiProviderAdapter inner) : IProductionAiProviderAdapter
{
    public string ProviderId => inner.ProviderId;
    public Task<AiProviderAdapterResult>? Pending { get; private set; }
    public Task<AiProviderAdapterResult> CompleteAsync(string model, string credential, AiProviderRequest request, CancellationToken cancellationToken)
        => Pending = inner.CompleteAsync(model, credential, request, cancellationToken);
}

sealed class ScriptedAdapter(string providerId, Queue<AiProviderFailure> failures) : IProductionAiProviderAdapter
{
    public ScriptedAdapter(string providerId, IEnumerable<AiProviderFailure> failures) : this(providerId, new Queue<AiProviderFailure>(failures)) { }
    public string ProviderId => providerId; public int Calls { get; private set; } public List<AiProviderRequest> Requests { get; } = [];
    public Task<AiProviderAdapterResult> CompleteAsync(string model, string credential, AiProviderRequest request, CancellationToken cancellationToken)
    {
        Calls++; Requests.Add(request); if (failures.TryDequeue(out var failure)) throw new AiProviderExecutionException(failure, failure.ToString());
        var content = request.OutputContract == AiProviderOutputContract.ProactiveSuggestion
            ? """{"summary":"ok","suggestedNextStep":"review","taskDraft":{"title":"follow up","description":"review needs"}}"""
            : "{\"summary\":\"ok\",\"suggestedNextAction\":null,\"attentionPoints\":[]}";
        return Task.FromResult(new AiProviderAdapterResult(providerId, model, content, TimeSpan.FromMilliseconds(1)));
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
sealed class FakeStore(ActiveWorkspaceAiPolicy? active) : IWorkspaceAiConfigurationStore
{
    public WorkspaceAiConfigurationState? State { get; set; }
    public string? ProtectedCredential { get; init; }
    public int ValidationWrites { get; private set; }
    public string? LastWorkspaceId { get; private set; }
    public Func<string,string,long,AiConfigurationCommit>? CredentialCommit { get; init; }
    public Func<string,long,AiConfigurationCommit>? DisableCommit { get; init; }
    public Task<ActiveWorkspaceAiPolicy?> FindActivePolicyAsync(string workspaceId, CancellationToken cancellationToken) { LastWorkspaceId = workspaceId; return Task.FromResult(active); }
    public Task<WorkspaceAiConfigurationState?> FindAsync(string workspaceId, CancellationToken cancellationToken) { LastWorkspaceId = workspaceId; return Task.FromResult(State); }
    public Task<string?> FindProtectedCredentialAsync(string workspaceId, bool fallback, CancellationToken cancellationToken) => Task.FromResult(ProtectedCredential);
    public Task RecordTestOutcomeAsync(string workspaceId,string memberId,bool succeeded,string provider,string model,string correlationId,DateTimeOffset now,CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<AiConfigurationCommit> SaveDraftAsync(string workspaceId,string memberId,WorkspaceAiConfigurationDraft draft,long expectedVersion,string idempotencyKey,string fingerprint,string correlationId,DateTimeOffset now,CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AiConfigurationCommit> SetCredentialAsync(string workspaceId,string memberId,bool fallback,string protectedCredential,long expectedVersion,string idempotencyKey,string fingerprint,string correlationId,DateTimeOffset now,CancellationToken cancellationToken)
        => Task.FromResult(CredentialCommit?.Invoke(workspaceId, protectedCredential, expectedVersion) ?? throw new NotSupportedException());
    public Task<AiConfigurationCommit> SetValidationAsync(string workspaceId,string memberId,bool succeeded,long expectedVersion,string idempotencyKey,string fingerprint,string correlationId,DateTimeOffset now,CancellationToken cancellationToken)
    {
        if (State?.Version != expectedVersion) return Task.FromResult(new AiConfigurationCommit(AiConfigurationCommitStatus.VersionConflict));
        ValidationWrites++;
        State = State! with { IsValidated = succeeded, Version = expectedVersion + 1 };
        return Task.FromResult(new AiConfigurationCommit(AiConfigurationCommitStatus.Committed, State));
    }
    public Task<AiConfigurationCommit> ActivateAsync(string workspaceId,string memberId,long expectedVersion,string idempotencyKey,string fingerprint,string correlationId,DateTimeOffset now,CancellationToken cancellationToken)
        => Task.FromResult(new AiConfigurationCommit(AiConfigurationCommitStatus.Committed, State));
    public Task<AiConfigurationCommit> DisableAsync(string workspaceId,string memberId,long expectedVersion,string idempotencyKey,string fingerprint,string correlationId,DateTimeOffset now,CancellationToken cancellationToken)
        => Task.FromResult(DisableCommit?.Invoke(workspaceId, expectedVersion) ?? throw new NotSupportedException());
}

sealed class AllowAuthorizer : IAccessAuthorizer
{
    public Task<AccessAuthorizationDecision> AuthorizeAsync(AccessRequirement requirement, string correlationId, CancellationToken cancellationToken)
        => Task.FromResult(new AccessAuthorizationDecision(true, "ALLOWED", null));
}
sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "AiProviderVerifier";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
sealed class ManualDeadlineClock : TimeProvider
{
    private long ticks;
    private readonly List<DeadlineTimer> timers = [];
    public int TimerCount => timers.Count;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => ticks;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(ticks);
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new DeadlineTimer(this, callback, state);
        timers.Add(timer); timer.Change(dueTime, period); return timer;
    }
    public void Advance(TimeSpan by)
    {
        ticks += by.Ticks;
        foreach (var timer in timers.ToArray()) timer.FireIfDue();
    }
    private sealed class DeadlineTimer(ManualDeadlineClock owner, TimerCallback callback, object? state) : ITimer
    {
        private long due = long.MaxValue;
        private bool disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (disposed) return false;
            if (period != Timeout.InfiniteTimeSpan) throw new NotSupportedException("Only one-shot deadline timers are used by this fixture.");
            due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner.ticks + dueTime.Ticks;
            return true;
        }
        public void FireIfDue() { if (!disposed && due <= owner.ticks) { due = long.MaxValue; callback(state); } }
        public void Dispose() { disposed = true; owner.timers.Remove(this); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
sealed class BlockingReadStream(TaskCompletionSource entered) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        entered.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
sealed class SafeTestLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Messages.Add(formatter(state, exception));
}
