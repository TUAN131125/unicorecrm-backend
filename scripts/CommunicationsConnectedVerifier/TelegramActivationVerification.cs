using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using UnicoreCRM.ApiHost;
using UnicoreCRM.ApiHost.Development;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Communications.Infrastructure.Telegram;
using UnicoreCRM.Platform.Workspace.Contracts;

internal sealed partial class TelegramConnectionVerification
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<object> contractSamples = new();
    private sealed class NormalHost(Dictionary<string, string?> configuration, string contentRoot,
        Action<IServiceCollection> configure) : WebApplicationFactory<ApiHostAssembly>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Minimal-host registrations read configuration before ConfigureWebHost callbacks run.
            builder.ConfigureHostConfiguration(source => source.AddInMemoryCollection(configuration));
            return base.CreateHost(builder);
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(contentRoot).UseEnvironment("SecurityVerification");
            builder.ConfigureAppConfiguration((_, source) => { source.Sources.Clear(); source.AddInMemoryCollection(configuration); });
            builder.ConfigureLogging(log => log.ClearProviders());
            builder.ConfigureTestServices(configure);
        }
    }

    internal async Task RunNormalActivationAsync(string connection, string signingKey, IReadOnlyDictionary<string, string> tokens,
        Func<string, Task> sql, Func<string, Task<object?>> scalar, Action<bool, string> check)
    {
        var address = fake.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var configuration = new Dictionary<string, string?> {
            ["ConnectionStrings:UnicoreCRM"] = connection,
            ["IdentityAuth:Jwt:SigningKey"] = signingKey, ["IdentityAuth:RefreshTokenPepper"] = signingKey,
            ["IdentityAuth:Jwt:Issuer"] = "Com1Fixture", ["IdentityAuth:Jwt:Audience"] = "Com1Fixture",
            ["IdentityAuth:EmailVerification:Sender:Kind"] = "Unavailable",
            ["AI:DataProtection:KeyRingPath"] = Path.Combine(root, "normal-ai"),
            ["Communications:Telegram:Enabled"] = "false"
        };
        void Configure(IServiceCollection services) {
            services.AddSingleton<ITelegramCredentialCustody>(custody);
            services.AddSingleton(new TelegramProviderAdmission(true, true));
            services.AddHttpClient<TelegramApiClient>(client => client.BaseAddress = new Uri(address));
        }
        await using var factory = new NormalHost(configuration, root, Configure);
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        // Actual normal startup/module registrations, no hosted-service substitution or alternate Program.
        await factory.Services.RunOwnerSchemaMigrationsAsync(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default);
        var activeAccount = await RunActivationAsync(factory.Services, http, tokens, sql, scalar, check);
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/communications/", StringComparison.Ordinal) == true)
            .Select(e => new { path = e.RoutePattern.RawText, operationId = e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
                methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods,
                authenticated = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0,
                trustedWorkspace = e.Metadata.GetMetadata<WorkspaceRequiredMetadata>() is not null }).ToArray();
        await File.WriteAllTextAsync(Path.Combine(artifactRoot, "normal-api-contract-evidence.json"),
            JsonSerializer.Serialize(new { endpoints, responses = contractSamples.ToArray() }));
        var previousRoot = factory.Services;
        await factory.DisposeAsync();
        await using var restarted = new NormalHost(configuration, root, Configure);
        using var restartedHttp = restarted.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        restarted.Services.GetRequiredService<TelegramPollingOptions>().Enabled = true;
        var offset = Convert.ToInt64(await scalar($"SELECT NextOffset FROM communications.TelegramPolling WHERE AccountId='{activeAccount}'"));
        var messages = Convert.ToInt64(await scalar($"SELECT COUNT(*) FROM communications.Messages WHERE AccountId='{activeAccount}'"));
        await sql($"UPDATE communications.TelegramPolling SET RetryAfter=NULL WHERE AccountId='{activeAccount}'");
        await using var scope = restarted.Services.CreateAsyncScope();
        check(!ReferenceEquals(previousRoot, restarted.Services)
            && await scope.ServiceProvider.GetRequiredService<TelegramInboundCoordinator>().PollAsync("ws_com1_b", activeAccount) == "POLL_COMMITTED"
            && Convert.ToInt64(await scalar($"SELECT NextOffset FROM communications.TelegramPolling WHERE AccountId='{activeAccount}'")) == offset
            && Convert.ToInt64(await scalar($"SELECT COUNT(*) FROM communications.Messages WHERE AccountId='{activeAccount}'")) == messages,
            "normal ApiHost host recreation recovers activated generation checkpoint and deduplication");
        using var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"/communications/channel-accounts/{activeAccount}/connection");
        statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens["a"]);
        statusRequest.Headers.Add("X-Workspace-Id", "ws_com1_b"); statusRequest.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
        statusRequest.Headers.Add("X-Correlation-Id", "activation-host-restart");
        using var statusResponse = await restartedHttp.SendAsync(statusRequest);
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        check(statusResponse.IsSuccessStatusCode && status.RootElement.GetProperty("health").GetString() == "HEALTHY",
            "recreated normal host reports health only after fresh successful polling");
    }

    private async Task<Guid> RunActivationAsync(IServiceProvider services, HttpClient http, IReadOnlyDictionary<string, string> tokens,
        Func<string, Task> sql, Func<string, Task<object?>> scalar, Action<bool, string> check)
    {
        async Task<(int Status, JsonElement Body)> Request(string method, string path, object? body = null,
            string actor = "admin", string workspace = "ws_com1_a", string? key = null, string? requestId = null)
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[actor]);
            req.Headers.Add("X-Workspace-Id", workspace); req.Headers.Add("X-Request-Id", requestId ?? Guid.NewGuid().ToString("N"));
            req.Headers.Add("X-Correlation-Id", "offline-activation-fixture");
            if (body is not null) { req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"); req.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N")); }
            using var res = await http.SendAsync(req); var text = await res.Content.ReadAsStringAsync();
            check(!text.Contains("fixture-activation-") && !text.Contains("Ciphertext", StringComparison.OrdinalIgnoreCase) && !text.Contains("Exception"), "activation API omits credential and diagnostics");
            var result = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text).RootElement.Clone();
            // Disposable fixture response metadata only. Never capture credential request bodies or headers.
            contractSamples.Enqueue(new { method, path, status = (int)res.StatusCode, body = result });
            return ((int)res.StatusCode, result);
        }
        Task<long> Version(Guid id) => GetLong($"SELECT ConfigurationVersion FROM communications.ChannelAccounts WHERE Id='{id}'");
        async Task<long> GetLong(string query) => Convert.ToInt64(await scalar(query));
        string Path(Guid id, string suffix) => $"/communications/channel-accounts/{id}/{suffix}";
        Task<(int Status, JsonElement Body)> Command(Guid id, string action, long version, string actor = "admin", string workspace = "ws_com1_a", string? key = null) =>
            Request("POST", Path(id, action), new { expectedVersion = version }, actor, workspace, key);
        async Task<JsonElement> Status(Guid id, string actor = "admin", string workspace = "ws_com1_a") => (await Request("GET", Path(id, "connection"), actor: actor, workspace: workspace)).Body;
        async Task<Guid> Create(string name, string actor = "admin", string workspace = "ws_com1_a")
        {
            var result = await Request("POST", "/communications/channel-accounts", new { providerKey = "telegram", displayName = name }, actor, workspace);
            check(result.Status == 201, "normal ApiHost creates activation account"); return result.Body.GetProperty("account").GetProperty("id").GetGuid();
        }
        async Task Save(Guid id, string secret, string actor = "admin", string workspace = "ws_com1_a") =>
            check((await Request("PUT", Path(id, "credentials"), new { botToken = secret, expectedVersion = await Version(id) }, actor, workspace)).Status == 200, "normal ApiHost protected credential configured");
        async Task<string> Poll(Guid id, string workspace = "ws_com1_a") { await using var scope = services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<TelegramInboundCoordinator>().PollAsync(workspace, id); }
        Task Due(Guid id) => sql($"UPDATE communications.TelegramPolling SET RetryAfter=NULL WHERE AccountId='{id}'");
        var options = services.GetRequiredService<TelegramPollingOptions>(); options.Enabled = true;
        var a1 = await Create("Activation A1"); var a2 = await Create("Activation A2"); var b1 = await Create("Activation B1", "a", "ws_com1_b");
        check((await Request("GET", "/communications/channel-providers")).Status == 200
            && (await Request("GET", "/communications/channel-accounts")).Status == 200
            && (await Request("GET", $"/communications/channel-accounts/{a1}")).Status == 200,
            "normal ApiHost catalog and account read contract samples");
        check((await Request("PATCH", $"/communications/channel-accounts/{a1}", new { displayName = "Activation A1 renamed", expectedVersion = await Version(a1) })).Status == 200,
            "normal ApiHost rename contract sample");
        check((await Request("GET", "/communications/channel-accounts", requestId: "bad")).Status == 422,
            "normal trusted Workspace metadata rejects short request identifier with422");
        var unsupported = await Request("POST", "/communications/channel-accounts", new { providerKey = "gmail", displayName = "Metadata only unsupported disconnect" });
        check(unsupported.Status == 201 && (await Command(unsupported.Body.GetProperty("account").GetProperty("id").GetGuid(), "disconnect", 1)).Status == 501,
            "normal account metadata disconnect reports unsupported non-Telegram capability501");
        var secrets = new[] { "fixture-activation-one", "fixture-activation-two", "fixture-activation-three" };
        bots[secrets[0]] = new(701); bots[secrets[1]] = new(702); bots[secrets[2]] = new(801);
        await Save(a1, secrets[0]); await Save(a2, secrets[1]); await Save(b1, secrets[2], "a", "ws_com1_b");
        check((await Command(a1, "activate", await Version(a1))).Status == 409, "activation refuses unverified identity");
        var initialCalls = methods.Count;
        var verifyVersion = await Version(a1);
        var verified = await Command(a1, "verify", verifyVersion, key: "activation-verify-a1");
        check(verified.Status == 200 && verified.Body.GetProperty("health").GetString() == "READY_TO_ACTIVATE" && !verified.Body.GetProperty("active").GetBoolean(), "verification proves identity without activation or operational claim");
        check(await Poll(a1) == "MISSING_CREDENTIALS", "verified inactive account cannot resolve polling credential");
        var verifiedCalls = methods.Count;
        check((await Command(a1, "verify", verifyVersion, key: "activation-verify-a1")).Status == 200 && methods.Count == verifiedCalls && verifiedCalls > initialCalls, "verification replay performs no provider calls");
        check((await Command(a2, "verify", await Version(a2))).Status == 200
            && (await Command(b1, "verify", await Version(b1), "a", "ws_com1_b")).Status == 200, "two Workspaces verify three exclusive identities");
        var activationVersion = await Version(a1);
        var race = await Task.WhenAll(Command(a1, "activate", activationVersion, key: "activate-retry-a1"), Command(a1, "activate", activationVersion, key: "activate-retry-a1"));
        check(race.All(x => x.Status == 200) && await Version(a1) == activationVersion + 1, "double activation has one version/audit change");
        check((await Status(a1)).GetProperty("health").GetString() == "STARTING", "activation is starting until successful current poll");
        check((await Command(a2, "activate", await Version(a2))).Status == 200
            && (await Command(b1, "activate", await Version(b1), "a", "ws_com1_b")).Status == 200, "multi-account activation independent");
        object Text(long update, string text) => new { update_id = update, message = new { message_id = update, date = 1792108800,
            chat = new { id = 9_100_000_001L, type = "private" }, from = new { id = 9_100_000_001L, is_bot = false }, text } };
        bots[secrets[0]].Updates = JsonSerializer.Serialize(new[] { Text(1, "SYNTHETIC_ACTIVATION_X") });
        bots[secrets[1]].Updates = JsonSerializer.Serialize(new[] { Text(1, "SYNTHETIC_ACTIVATION_Y") });
        bots[secrets[2]].Updates = JsonSerializer.Serialize(new[] { Text(1, "SYNTHETIC_ACTIVATION_Z") });
        check(await Poll(a1) == "POLL_COMMITTED" && await Poll(a2) == "POLL_COMMITTED" && await Poll(b1, "ws_com1_b") == "POLL_COMMITTED", "normal ApiHost activated A1 A2 B1 ingest separately");
        check((await Status(a1)).GetProperty("health").GetString() == "HEALTHY" && await GetLong($"SELECT COUNT(*) FROM communications.Messages WHERE AccountId IN ('{a1}','{a2}','{b1}')") == 3, "health requires successful poll and Core message ownership correct");
        var testGrants = new List<string>();
        foreach (var capability in new[] { "communications.claim", "communications.transition", "communications.link", "contacts.read" })
            if (await GetLong($"SELECT COUNT(*) FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability='{capability}'") == 0)
            { await sql($"INSERT INTO access.RoleCapabilities VALUES ('role_a','{capability}')"); testGrants.Add(capability); }
        var inboxId = (await GetLong($"SELECT Id FROM communications.Conversations WHERE AccountId='{a1}'")).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var otherInboxId = (await GetLong($"SELECT Id FROM communications.Conversations WHERE AccountId='{b1}'")).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var inboxPath = "/communications/conversations/" + inboxId;
        var queue = await Request("GET", "/communications/conversations?view=unassigned&limit=100", actor: "a");
        check(queue.Status == 200 && queue.Body.GetProperty("items").EnumerateArray().Any(row => row.GetProperty("id").GetString() == inboxId)
            && !queue.Body.GetProperty("items").EnumerateArray().Any(row => row.GetProperty("id").GetString() == otherInboxId)
            && !queue.Body.ToString().Contains("SYNTHETIC_ACTIVATION_"), "normal ApiHost Unified Inbox queue owns Telegram ingress without foreign identity or body");
        check((await Request("GET", inboxPath + "/messages")).Status == 403
            && (await Request("GET", inboxPath + "/messages", actor: "a")).Status == 404,
            "normal Studio channel manager and unassigned employee cannot read protected Telegram body");
        var claimed = await Request("POST", inboxPath + "/claim", actor: "a");
        check(claimed.Status == 200 && (await Request("GET", inboxPath + "/messages", actor: "a")).Body.ToString().Contains("SYNTHETIC_ACTIVATION_X")
            && (await Request("GET", inboxPath, actor: "a")).Status == 200,
            "normal ApiHost authorized Claim exposes only current-assignee Telegram timeline");
        var transitioned = await Request("POST", inboxPath + "/transition", new { targetStatus = "OPEN", expectedVersion = claimed.Body.GetProperty("version").GetInt64() }, "a");
        check(transitioned.Status == 200, "normal ApiHost Telegram conversation uses existing lifecycle");
        await sql("INSERT INTO contacts.Contacts(ContactId,WorkspaceId,OwnerId,FullName,Status,Version,CreatedAt,UpdatedAt,Profile) VALUES ('normal_pilot_contact','ws_com1_a','member_a','Normal Pilot Fixture','active',0,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET(),'{}')");
        var candidates = await Request("GET", inboxPath + "/crm-candidates?targetType=CONTACT&search=Normal%20Pilot", actor: "a");
        check(candidates.Status == 200 && candidates.Body.EnumerateArray().Any(row => row.GetProperty("targetId").GetString() == "normal_pilot_contact"),
            "normal ApiHost suggestions use authorized CRM public reader");
        var linked = await Request("POST", inboxPath + "/crm-link", new { targetType = "CONTACT", targetId = "normal_pilot_contact", expectedVersion = transitioned.Body.GetProperty("version").GetInt64() }, "a");
        check(linked.Status == 200 && (await Request("GET", inboxPath + "/crm-link", actor: "a")).Body.GetProperty("state").GetString() == "AVAILABLE",
            "normal ApiHost manually links Telegram conversation through existing CRM authorization");
        check((await Request("POST", inboxPath + "/crm-link/unlink", new { expectedVersion = linked.Body.GetProperty("version").GetInt64() }, "a")).Status == 200,
            "normal ApiHost manual unlink preserves independent timeline");
        foreach (var capability in testGrants) await sql($"DELETE FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability='{capability}'");
        var offset = await GetLong($"SELECT NextOffset FROM communications.TelegramPolling WHERE AccountId='{a1}'");
        check((await Command(a1, "disconnect", await Version(a1))).Status == 200 && await Poll(a1) == "MISSING_CREDENTIALS", "disconnect fences only selected account");
        check((await Status(a1)).GetProperty("credentialPresent").GetBoolean(), "disconnect preserves protected credential for explicit reconnect");
        await Due(a2); await Due(b1);
        check(await Poll(a2) == "POLL_COMMITTED" && await Poll(b1, "ws_com1_b") == "POLL_COMMITTED", "A2 B1 continue during A1 disconnect");
        check((await Command(a1, "reconnect", await Version(a1))).Status == 200 && await Poll(a1) == "POLL_COMMITTED"
            && await GetLong($"SELECT NextOffset FROM communications.TelegramPolling WHERE AccountId='{a1}'") == offset
            && await GetLong($"SELECT COUNT(*) FROM communications.Messages WHERE AccountId='{a1}'") == 1, "same bot reconnect preserves checkpoint and replay deduplication");
        check((await Command(a1, "verify", verifyVersion, key: "activation-verify-a1")).Status == 409, "old replay cannot resurrect previous generation");
        check((await Command(a1, "activate", await Version(a1), "a", "ws_com1_b")).Status == 404
            && (await Command(a1, "activate", await Version(a1), "b")).Status == 403, "activation enforces Workspace and explicit manage");
        await Save(a2, secrets[1]);
        check(!(await Status(a2)).GetProperty("active").GetBoolean() && (await Status(a1)).GetProperty("active").GetBoolean(), "rotation affects only A2 verification and activation");
        var duplicate = await Create("Activation duplicate"); await Save(duplicate, secrets[0]);
        check((await Command(duplicate, "verify", await Version(duplicate))).Status == 409, "duplicate bot reservation hidden across accounts");
        var foreignDuplicate = await Create("Activation foreign duplicate", "a", "ws_com1_b"); await Save(foreignDuplicate, secrets[0], "a", "ws_com1_b");
        check((await Command(foreignDuplicate, "verify", await Version(foreignDuplicate), "a", "ws_com1_b")).Status == 409, "same bot cannot be reserved by another Workspace");
        var raceOne = await Create("Bot reservation race one"); var raceTwo = await Create("Bot reservation race two");
        bots["fixture-activation-race-one"] = new(703); bots["fixture-activation-race-two"] = new(703);
        await Save(raceOne, "fixture-activation-race-one"); await Save(raceTwo, "fixture-activation-race-two");
        var botRace = await Task.WhenAll(Command(raceOne, "verify", await Version(raceOne)), Command(raceTwo, "verify", await Version(raceTwo)));
        check(botRace.Count(x => x.Status == 200) == 1 && botRace.Count(x => x.Status == 409) == 1,
            "global SQL bot constraint admits exactly one concurrent verifier: " + string.Join(',', botRace.Select(x => x.Status)));
        bots[secrets[1]].Id = 999;
        check((await Command(a2, "verify", await Version(a2))).Status == 409, "rotated token for another bot cannot rebind history"); bots[secrets[1]].Id = 702;
        bots[secrets[1]].Webhook = true;
        check((await Command(a2, "reconnect", await Version(a2))).Body.GetProperty("code").GetString() == "WEBHOOK_CONFLICT"
            && (await Status(a2)).GetProperty("health").GetString() == "WEBHOOK_CONFLICT", "webhook conflict stops account without deleting webhook"); bots[secrets[1]].Webhook = false;
        foreach (var error in new[] { 401, 403 })
        {
            bots[secrets[1]].Error = error;
            check((await Command(a2, "verify", await Version(a2))).Body.GetProperty("code").GetString() == "REAUTH_REQUIRED"
                && (await Status(a2)).GetProperty("health").GetString() == "AUTH_FAILED", "verification auth error classified " + error);
        }
        bots[secrets[1]].Error = 0;
        foreach (var malformed in new[] { "[]", "null", "4" })
        {
            check((await Command(a2, "reconnect", await Version(a2))).Status == 200, "malformed webhook precondition active");
            bots[secrets[1]].WebhookResult = malformed;
            var invalid = await Command(a2, "verify", await Version(a2));
            check(invalid.Status == 503 && invalid.Body.GetProperty("code").GetString() == "RESPONSE_INVALID"
                && !(await Status(a2)).GetProperty("active").GetBoolean(), "malformed webhook safely invalidates previous activation: " + malformed);
            bots[secrets[1]].WebhookResult = null;
        }
        foreach (var malformed in new[] { "{\"ok\":false,\"error_code\":\"bad\"}", "{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":\"bad\"}}", "{\"ok\":false,\"error_code\":429,\"parameters\":[]}" })
        {
            check((await Command(a2, "reconnect", await Version(a2))).Status == 200, "malformed envelope precondition active");
            bots[secrets[1]].RawResponse = malformed;
            var invalid = await Command(a2, "verify", await Version(a2));
            check(invalid.Status == 503 && invalid.Body.GetProperty("code").GetString() == "RESPONSE_INVALID"
                && !(await Status(a2)).GetProperty("active").GetBoolean(), "malformed error metadata safely invalidates previous activation");
            bots[secrets[1]].RawResponse = null;
        }
        await Command(a2, "verify", await Version(a2));
        var rollbackVersion = await Version(a2);
        await sql("CREATE TRIGGER communications.RejectActivationAudit ON communications.ChannelMutations AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Operation='TELEGRAM_ACTIVATE') THROW 51000,'Task activation audit fault',1; END");
        check((await Command(a2, "activate", rollbackVersion)).Status == 500, "activation audit failure is not success");
        await sql("DROP TRIGGER communications.RejectActivationAudit");
        check(await Version(a2) == rollbackVersion && !(await Status(a2)).GetProperty("active").GetBoolean(), "failed audit rolls back activation version and lease atomically");
        await sql("INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('activation_status_fls','ws_com1_a','role_admin','communications.channels','status','ReadOnly')");
        var beforeDenied = methods.Count;
        check((await Command(a2, "activate", await Version(a2))).Status == 403 && methods.Count == beforeDenied, "activation respects status write FLS before provider I/O");
        await sql("DELETE FROM access.RoleFieldSecurity WHERE PolicyId='activation_status_fls'");
        var held = bots[secrets[1]]; held.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); held.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var rotateDuringActivation = Command(a2, "activate", await Version(a2)); await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Save(a2, secrets[1]); held.Release.SetResult();
        check((await rotateDuringActivation).Status == 409 && !(await Status(a2)).GetProperty("active").GetBoolean(), "credential rotation during activation cannot commit stale generation");
        held.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); held.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = Command(a2, "verify", await Version(a2)); await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Command(a2, "disable", await Version(a2)); held.Release.SetResult();
        check((await delayed).Status == 409 && !(await Status(a2)).GetProperty("active").GetBoolean(), "late verification cannot undo concurrent disable"); held.Entered = null; held.Release = null;
        await Command(a2, "enable", await Version(a2));
        held.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); held.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var revoked = Command(a2, "verify", await Version(a2)); await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await sql("DELETE FROM access.RoleCapabilities WHERE RoleId='role_admin' AND Capability='communications.channels.manage'"); held.Release.SetResult();
        check((await revoked).Status == 403, "authority revoked during getMe prevents verification commit");
        await sql("INSERT INTO access.RoleCapabilities VALUES ('role_admin','communications.channels.manage')"); held.Entered = null; held.Release = null;
        check((await Command(a1, "deactivate", await Version(a1))).Status == 200 && await Poll(a1) == "MISSING_CREDENTIALS", "explicit deactivation preserves configured identity and stops polling");
        check(methods.All(x => x is "getMe" or "getWebhookInfo" or "getUpdates"), "activation reconnect runtime have zero outbound or webhook mutation methods");
        options.Enabled = false;
        await sql($"UPDATE communications.Conversations SET Status='CLOSED' WHERE AccountId IN ('{a1}','{a2}','{b1}')");
        return b1;
    }
}
