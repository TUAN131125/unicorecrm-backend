using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using UnicoreCRM.AI.Gateway;
using UnicoreCRM.AI.Providers;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

internal static class OpenAiModelVerifier
{
    private const string Success = """{"id":"response-current","status":"completed","output":[{"type":"reasoning","summary":[]},{"type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","text":"{\"summary\":\"o"},{"type":"output_text","text":"k\",\"attentionPoints\":[],\"suggestedNextAction\":null}"}]}],"usage":{"input_tokens":13,"output_tokens":17}}""";

    internal static async Task VerifyAsync(AiProviderRequest request)
    {
        string[] expected = ["gpt-6-luna", "gpt-6.1-sol", "gpt-6-astra", "gpt-5.6-sol", "gpt-6-sol", "gpt-5.6-terra", "gpt-5.6-luna"];
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var catalog = new AiProviderCatalog(config);
        var visible = catalog.View().Providers.Single(x => x.Id == "OPENAI").Models;
        Check(visible.Select(x => x.Id).SequenceEqual(expected) && visible.All(x => x.StructuredOutput), "Current catalog IDs/order/structured support");
        Check(expected.All(x => catalog.IsAdmitted("OPENAI", x)) && !catalog.IsAdmitted("OPENAI", "gpt-6")
            && !catalog.IsAdmitted("OPENAI", "unknown-model") && !catalog.IsAdmitted("GEMINI", expected[0]), "Admission is exact and provider scoped");
        var credential = Guid.NewGuid().ToString("N");
        foreach (var model in expected)
        {
            foreach (var isTest in new[] { false, true })
            {
                using var http = Client(async (message, ct) =>
                {
                    Check(message.Method == HttpMethod.Post && message.RequestUri?.AbsoluteUri == "https://api.openai.com/v1/responses", "Responses endpoint");
                    Check(message.Headers.Authorization is { Scheme: "Bearer" } && message.Headers.Authorization.Parameter == credential, "Header-only credential");
                    using var json = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
                    var root = json.RootElement;
                    Check(root.GetProperty("model").GetString() == model && !root.GetProperty("store").GetBoolean(), "Exact model and no response storage");
                    Check(root.GetProperty("reasoning").GetProperty("effort").GetString() == (isTest ? "low" : "medium"), "Valid reasoning by workload, never none for Astra/Sol");
                    Check(root.GetProperty("max_output_tokens").GetInt32() == 2048 && !root.TryGetProperty("temperature", out _)
                        && !root.TryGetProperty("top_p", out _) && !root.TryGetProperty("tools", out _) && !root.TryGetProperty("stream", out _), "Bounded output, no unsupported sampler/tool/stream parameters");
                    var format = root.GetProperty("text").GetProperty("format");
                    var schema = format.GetProperty("schema");
                    Check(format.GetProperty("type").GetString() == "json_schema" && format.GetProperty("strict").GetBoolean()
                        && !schema.GetProperty("additionalProperties").GetBoolean(), "Strict Responses structured output");
                    Check(schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).SequenceEqual(["summary", "suggestedNextAction", "attentionPoints"]), "CRM output contract is unchanged");
                    return Json(HttpStatusCode.OK, Success);
                });
                var response = await new OpenAiProvider(http, TimeProvider.System).CompleteAsync(model, credential, request with { IsConfigurationTest = isTest }, default);
                Check(new AiProviderOutputValidator().Validate(response.Content)?.Summary == "ok" && response.InputTokens == 13 && response.OutputTokens == 17,
                    "Reasoning item and multiple text parts resolve to validated advisory");
            }
        }

        var protection = new EphemeralDataProtectionProvider();
        var protectedValue = protection.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1").Protect(credential);
        foreach (var model in new[] { "gpt-5-mini", "gpt-5" })
        {
            Check(catalog.IsAdmitted("OPENAI", model) && visible.All(x => x.Id != model), "Legacy execution survives, legacy is not offered to new configurations");
            var store = new FakeStore(new(new("OPENAI", model, "WORKSPACE", false, null, null, null, false), protectedValue, null))
                { State = State(model) with { Status = AiConfigurationValues.Active, IsValidated = true }, ProtectedCredential = protectedValue };
            var app = App(store, new ScriptedAdapter("OPENAI", []));
            Check((await app.GetAsync("legacy-read", default)).Value?.PrimaryModel == model, "Stored legacy model reads unchanged");
            var resolved = await new WorkspaceAiProviderResolver(new FakeWorkspace(), store, protection, config, catalog).ResolveAsync(default);
            Check(resolved.Primary.Model == model && resolved.Primary.Credential == credential && resolved.Fallback is null && store.LastWorkspaceId == "workspace-a", "Legacy active policy, Workspace credential and trusted scope are preserved without implicit fallback");
        }
        foreach (var configuredModel in new string?[] { null, "gpt-5-mini" })
        {
            var values = new Dictionary<string,string?> { ["AI:DeploymentDefault:Provider"] = "OPENAI", ["AI:Providers:OPENAI:ApiKey"] = credential };
            if (configuredModel is not null) values["AI:DeploymentDefault:Model"] = configuredModel;
            var deploymentConfig = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var resolver = new WorkspaceAiProviderResolver(new FakeWorkspace(), new FakeStore(null), protection, deploymentConfig, new(deploymentConfig));
            Check((await resolver.ResolveAsync(default)).Primary.Model == (configuredModel ?? "gpt-6-luna"), "Only an unspecified OpenAI deployment model gets the new default");
        }

        foreach (var (status, failure) in new[] {
            (HttpStatusCode.BadRequest, AiProviderFailure.InvalidRequest), (HttpStatusCode.Unauthorized, AiProviderFailure.AuthenticationFailure),
            (HttpStatusCode.Forbidden, AiProviderFailure.PermissionDenied), (HttpStatusCode.NotFound, AiProviderFailure.ModelUnavailable),
            (HttpStatusCode.TooManyRequests, AiProviderFailure.RateLimited), (HttpStatusCode.InternalServerError, AiProviderFailure.Unavailable),
            (HttpStatusCode.ServiceUnavailable, AiProviderFailure.Unavailable), (HttpStatusCode.GatewayTimeout, AiProviderFailure.Timeout) })
        {
            var calls = 0;
            using var http = Client((_, _) => { calls++; return Task.FromResult(Json(status, "untrusted-body-must-not-be-retained")); });
            try { await new OpenAiProvider(http, TimeProvider.System).CompleteAsync(expected[0], credential, request, default); throw new InvalidOperationException("Expected HTTP failure."); }
            catch (AiProviderExecutionException error) { Check(error.Failure == failure && error.ProviderHttpStatus == (int)status && calls == 1
                && !error.ToString().Contains("untrusted-body", StringComparison.Ordinal) && !error.ToString().Contains(credential, StringComparison.Ordinal), "Safe HTTP status/classification with no adapter retry"); }
        }
        foreach (var invalid in new[] { "{bad", "[]", """{"status":"completed","output":[]}""", """{"status":"completed","output":[null]}""",
            """{"status":"completed","output":[{"content":[{"type":"output_text","text":""}]}]}""",
            """{"output":[{"content":[{"type":"output_text","text":"{}"}]}]}""",
            """{"status":"completed","output":[{"content":[{"type":"output_text","text":42}]}]}""",
            """{"status":"completed","output":[{"content":[{"type":"output_text","text":"not JSON"}]}]}""",
            """{"status":"completed","output":[{"content":[{"type":"output_text","text":"{\"summary\":\"missing attention\"}"}]}]}""",
            Success.Replace("\"status\":\"completed\"", "\"status\":\"incomplete\"", StringComparison.Ordinal),
            Success.Replace("\"status\":\"completed\"", "\"status\":\"failed\"", StringComparison.Ordinal) })
        {
            var calls = 0;
            using var http = Client((_, _) => { calls++; return Task.FromResult(Json(HttpStatusCode.OK, invalid)); });
            var store = new FakeStore(null) { State = State(expected[0]), ProtectedCredential = protectedValue };
            var app = App(store, new OpenAiProvider(http, TimeProvider.System));
            Check((await app.TestAsync(7, "invalid-output", "invalid-correlation", default)).Error?.Code == "AI_PROVIDER_RESPONSE_INVALID"
                && calls == 1 && store.ValidationWrites == 0 && store.State?.IsValidated == false, "Malformed/incomplete output cannot validate or trigger another request");
            Check((await app.ActivateAsync(7, "invalid-activate", "invalid-correlation", default)).Error?.Code == "AI_CONFIGURATION_NOT_VALIDATED", "Failed OpenAI Test cannot Activate");
            Check(store.ProtectedCredential == protectedValue, "Failure does not mutate credentials");
        }
        using (var http = Client((_, _) => Task.FromResult(Json(HttpStatusCode.OK, Success))))
        {
            var observed = new ObservedAdapter(new OpenAiProvider(http, TimeProvider.System));
            var store = new FakeStore(null) { State = State(expected[0]), ProtectedCredential = protectedValue };
            var app = App(store, observed);
            Check((await app.TestAsync(6, "stale", "version-correlation", default)).Error?.Code == "AI_CONFIGURATION_VERSION_CONFLICT" && observed.Pending is null, "Stale Test is rejected before HTTP");
            var result = await app.TestAsync(7, "success", "version-correlation", default);
            Check(result.Value?.Configuration is { IsValidated:true, Version:8 }, "Only valid current-model output validates");
            Check((await app.ActivateAsync(7, "stale-activate", "version-correlation", default)).Error?.Code == "AI_CONFIGURATION_VERSION_CONFLICT"
                && (await app.ActivateAsync(8, "activate", "version-correlation", default)).IsSuccess, "Activate requires the latest validated version (mock only)");
        }
        using (var http = Client(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Json(HttpStatusCode.OK, Success); }))
        {
            http.Timeout = TimeSpan.FromMilliseconds(25);
            try { await new OpenAiProvider(http, TimeProvider.System).CompleteAsync(expected[0], credential, request, default); throw new InvalidOperationException("Expected timeout."); }
            catch (AiProviderExecutionException error) { Check(error.Failure == AiProviderFailure.Timeout && error.TimeoutSource == AiProviderTimeoutSource.HttpClientDeadline, "HttpClient deadline is distinct from caller cancellation"); }
        }
        using (var caller = new CancellationTokenSource())
        using (var http = Client(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Json(HttpStatusCode.OK, Success); }))
        {
            var pending = new OpenAiProvider(http, TimeProvider.System).CompleteAsync(expected[0], credential, request, caller.Token);
            caller.Cancel();
            try { await pending; throw new InvalidOperationException("Expected caller cancellation."); }
            catch (OperationCanceledException) when (caller.IsCancellationRequested) { }
        }

        // Exercise command boundaries with a recording store, not a simulated database.
        var rotatedCredential = Guid.NewGuid().ToString("N");
        var rotationCalls = 0;
        var disableCalls = 0;
        var mutationState = State(expected[0]);
        var mutationStore = new FakeStore(null)
        {
            State = mutationState,
            ProtectedCredential = protectedValue,
            CredentialCommit = (workspace, ciphertext, version) =>
            {
                rotationCalls++;
                Check(workspace == "workspace-a" && version == 7 && ciphertext != rotatedCredential
                    && protection.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1").Unprotect(ciphertext) == rotatedCredential,
                    "Credential rotation reaches only the trusted Workspace store as protected material");
                return new(AiConfigurationCommitStatus.VersionConflict);
            },
            DisableCommit = (workspace, version) =>
            {
                disableCalls++;
                Check(workspace == "workspace-a" && version == 7, "Disable retains the trusted Workspace and expected version");
                return new(AiConfigurationCommitStatus.Committed, mutationState with { Status = AiConfigurationValues.Disabled, Version = 8 });
            }
        };
        var mutations = App(mutationStore, new ScriptedAdapter("OPENAI", []));
        var rotation = await mutations.SetCredentialAsync(new(rotatedCredential, false), 7, "rotate", "rotation-correlation", default);
        Check(rotation.Error?.Code == "AI_CONFIGURATION_VERSION_CONFLICT" && rotationCalls == 1
            && mutationStore.ProtectedCredential == protectedValue, "A conflicting credential mutation is not retried and preserves the existing credential");
        var disabled = await mutations.DisableAsync(7, "disable", "disable-correlation", default);
        Check(disabled.Value?.Configuration.Status == AiConfigurationValues.Disabled && disableCalls == 1,
            "Disable maps the store result without an implicit Activate or mutation retry");
        Console.WriteLine("OPENAI_CURRENT_MODELS_MOCK_PASS (catalog, legacy, Responses, reasoning, validation, errors, cancellation)");

        WorkspaceAiConfigurationState State(string model) => new("workspace-a", AiConfigurationValues.Draft, "OPENAI", model, "WORKSPACE",
            true, false, null, null, null, false, false, 7, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);
        AiConfigurationApplication App(FakeStore store, IProductionAiProviderAdapter adapter) => new(new FakeWorkspace(), new AllowAuthorizer(), store,
            new FakeLedger(), catalog, protection, config, [adapter], new(), new(TimeSpan.FromSeconds(1)), TimeProvider.System, new SafeTestLogger<AiConfigurationApplication>());
    }

    private static HttpClient Client(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> handler) => new(new StubHandler(handler)) { BaseAddress = new("https://api.openai.com/") };
    private static HttpResponseMessage Json(HttpStatusCode status, string value) => new(status) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
