using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Communications.Application;
using UnicoreCRM.Communications.Infrastructure.Persistence;
using UnicoreCRM.Communications.Infrastructure.Telegram;

internal sealed partial class TelegramConnectionVerification : IAsyncDisposable
{
    private sealed class Custody(IDataProtectionProvider provider) : ITelegramCredentialCustody
    {
        public bool SyntheticReady { get; set; } = true;
        public IDataProtectionProvider? Protection => provider;
    }
    private sealed class Bot(long id)
    {
        internal long Id = id;
        internal int Error;
        internal bool Webhook;
        internal string? WebhookResult;
        internal string? RawResponse;
        internal TaskCompletionSource? Entered;
        internal TaskCompletionSource? Release;
        internal string Updates = "[]";
    }
    private readonly string root;
    private readonly string artifactRoot;
    private readonly X509Certificate2 certificate;
    private readonly ServiceProvider keys;
    private readonly Custody custody;
    private readonly WebApplication fake;
    private readonly ConcurrentDictionary<string, Bot> bots = new();
    private readonly ConcurrentQueue<string> methods = new();
    private TelegramConnectionVerification(string artifacts, string database, IServiceCollection services)
    {
        artifactRoot = Path.GetFullPath(artifacts); root = Path.Combine(artifactRoot, "test-keyrings", database); Directory.CreateDirectory(root);
        using var rsa = RSA.Create(2048);
        certificate = new CertificateRequest("CN=UniCoreCRM synthetic custody", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        keys = KeyServices(Path.Combine(root, "runtime")); custody = new(keys.GetRequiredService<IDataProtectionProvider>());
        services.AddSingleton<ITelegramCredentialCustody>(custody);
        services.AddSingleton(new TelegramProviderAdmission(true, true));
        var browserUpdates = JsonSerializer.Serialize(new[] { new { update_id = 1, message = new { message_id = 1, date = 1792108800,
            chat = new { id = 9_200_000_001L, type = "private" }, from = new { id = 9_200_000_001L, is_bot = false }, text = "SYNTHETIC_BROWSER_ACTIVATED_TEXT" } } });
        bots["fixture-browser-credential"] = new(1201) { Updates = browserUpdates };
        bots["fixture-browser-replacement"] = new(1201) { Updates = browserUpdates };
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0"); fake = builder.Build();
        fake.MapPost("/bot{token}/{method}", async (string token, string method, HttpContext context) =>
        {
            methods.Enqueue(method);
            if (!bots.TryGetValue(token, out var bot)) { context.Response.StatusCode = 401; return; }
            if (bot.Entered is { } entered && method == "getMe") { entered.TrySetResult(); if (bot.Release is { } release) await release.Task.WaitAsync(context.RequestAborted); }
            if (bot.Error != 0) { context.Response.StatusCode = bot.Error; return; }
            context.Response.ContentType = "application/json";
            if (bot.RawResponse is { } raw) { await context.Response.WriteAsync(raw); return; }
            if (method == "getWebhookInfo" && bot.WebhookResult is { } result) { await context.Response.WriteAsync("{\"ok\":true,\"result\":" + result + "}"); return; }
            if (method == "getMe") await context.Response.WriteAsJsonAsync(new { ok = true, result = new { id = bot.Id, is_bot = true } });
            else if (method == "getWebhookInfo") await context.Response.WriteAsJsonAsync(new { ok = true, result = new { url = bot.Webhook ? "https://example.invalid/hook" : "" } });
            else if (method == "getUpdates") await context.Response.WriteAsync("{\"ok\":true,\"result\":" + bot.Updates + "}");
            else context.Response.StatusCode = 405;
        });
    }
    private ServiceProvider KeyServices(string directory)
    {
        Directory.CreateDirectory(directory); var services = new ServiceCollection(); services.AddLogging();
        services.AddDataProtection().SetApplicationName("UniCoreCRM.Communications.Credentials")
            .PersistKeysToFileSystem(new DirectoryInfo(directory)).ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
        return services.BuildServiceProvider();
    }
    internal static async Task<TelegramConnectionVerification> StartAsync(string artifacts, string database, IServiceCollection services)
    {
        var fixture = new TelegramConnectionVerification(artifacts, database, services); await fixture.fake.StartAsync();
        var address = fixture.fake.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        services.AddHttpClient<TelegramApiClient>(client => client.BaseAddress = new Uri(address)); return fixture;
    }

    internal async Task RunAsync(IServiceProvider services, HttpClient http, IReadOnlyDictionary<string, string> tokens,
        Func<string, Task> sql, Func<string, Task<object?>> scalar, Action<bool, string> check)
    {
        var secrets = new[] { "fixture-secure-one", "fixture-secure-two", "fixture-secure-three", "fixture-secure-rotated" };
        bots[secrets[0]] = new(301); bots[secrets[1]] = new(302); bots[secrets[2]] = new(401); bots[secrets[3]] = new(301);
        async Task<(int Status, JsonElement Body)> Request(string method, string path, object? body = null,
            string actor = "admin", string workspace = "ws_com1_a", string? key = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[actor]);
            request.Headers.Add("X-Workspace-Id", workspace); request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N")); request.Headers.Add("X-Correlation-Id", "synthetic-credential-operation");
            if (body is not null) { request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"); request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N")); }
            using var response = await http.SendAsync(request); var text = await response.Content.ReadAsStringAsync();
            check(secrets.All(s => !text.Contains(s, StringComparison.Ordinal)) && !text.Contains("Ciphertext", StringComparison.OrdinalIgnoreCase), "credential HTTP never returns secret/ciphertext");
            return ((int)response.StatusCode, JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text).RootElement.Clone());
        }
        async Task<Guid> Create(string name, string actor = "admin", string workspace = "ws_com1_a")
        {
            var result = await Request("POST", "/communications/channel-accounts", new { providerKey = "telegram", displayName = name }, actor, workspace);
            check(result.Status == 201, "credential fixture account created by canonical public API"); return result.Body.GetProperty("account").GetProperty("id").GetGuid();
        }
        string PathFor(Guid id) => $"/communications/channel-accounts/{id}";
        Task<(int Status, JsonElement Body)> Save(Guid id, string value, long version, string? key = null) => Request("PUT", PathFor(id) + "/credentials", new { botToken = value, expectedVersion = version }, key: key);
        async Task<long> Version(Guid id) => Convert.ToInt64(await scalar($"SELECT ConfigurationVersion FROM communications.ChannelAccounts WHERE Id='{id}'"));
        async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action) { await using var scope = services.CreateAsyncScope(); return await action(scope.ServiceProvider); }
        async Task Verify(Guid id) { await InScope(async sp => { await sp.GetRequiredService<TelegramConnectionManagement>().VerifySyntheticAsync("ws_com1_a", id); return 0; });
            await sql($"UPDATE communications.TelegramPolling SET Active=1,ActivatedAt=SYSDATETIMEOFFSET() WHERE AccountId='{id}'"); }
        async Task Reject(Func<Task> action, string code) { try { await action(); throw new InvalidOperationException("expected " + code); } catch (TelegramFailure e) { check(e.Code == code, "connection safely rejects " + code); } }

        var a1 = await Create("Secure Telegram A1"); var a2 = await Create("Secure Telegram A2"); var b1 = await Create("Secure Telegram B1", "a", "ws_com1_b");
        check(!(await Request("GET", PathFor(a1) + "/connection")).Body.GetProperty("credentialPresent").GetBoolean(), "draft starts without credentials");
        var one = await Save(a1, secrets[0], 1, "credential-stable-key");
        check(one.Status == 200 && one.Body.GetProperty("configurationState").GetString() == "CONFIGURED" && one.Body.GetProperty("verificationState").GetString() == "UNVERIFIED", "save is configured unverified without provider call");
        check(methods.IsEmpty, "credential save never verifies or polls automatically");
        check((await Save(a1, secrets[0], 1, "credential-stable-key")).Status == 200 && await Version(a1) == 2, "exact same credential request replays without version bump");
        check((await Save(a1, secrets[3], 1, "credential-stable-key")).Status == 409, "same idempotency key changed secret conflicts without echo");
        check((await Request("PUT", PathFor(a1) + "/credentials", new { botToken = secrets[0], expectedVersion = 2, workspaceId = "ws_com1_b" })).Status == 400, "credential JSON rejects client Workspace authority");
        check((await Request("PUT", PathFor(a1) + "/credentials", new { botToken = 42, expectedVersion = 2 })).Status == 400, "credential JSON rejects wrong token type without echo");
        await Save(a2, secrets[1], 1);
        check((await Request("PUT", PathFor(b1) + "/credentials", new { botToken = secrets[2], expectedVersion = 1 }, "a", "ws_com1_b")).Status == 200, "Workspace B has independent protected credential");
        check((await Request("GET", PathFor(a1) + "/connection", actor: "a", workspace: "ws_com1_b")).Status == 404, "cross Workspace connection read hidden");
        check((await Request("PUT", PathFor(a1) + "/credentials", new { botToken = secrets[1], expectedVersion = 2 }, "a", "ws_com1_b")).Status == 404, "cross Workspace credential mutation denied");
        check((await Request("GET", PathFor(a1) + "/connection", actor: "a")).Status == 403, "actor lacking explicit channels.manage cannot read credential metadata");
        await sql("INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('credential_fls','ws_com1_a','role_admin','communications.channels','status','ReadOnly')");
        check((await Save(a1, secrets[3], 2)).Status == 403, "credential mutation enforces channel write FLS");
        await sql("DELETE FROM access.RoleFieldSecurity WHERE PolicyId='credential_fls'");
        check((await Request("PUT", PathFor(a1) + "/credentials", new { botToken = secrets[1], expectedVersion = 2 }, "a")).Status == 403, "actor without manage cannot mutate credentials");
        check((await Request("POST", PathFor(a1) + "/verify")).Status == 400 && methods.IsEmpty, "public verify rejects missing version/idempotency before provider calls");
        await sql("CREATE TRIGGER communications.RejectCredentialAudit ON communications.ChannelMutations AFTER INSERT AS BEGIN THROW 51000,'Synthetic credential audit rejected',1; END");
        check((await Save(a1, secrets[3], 2)).Status == 500, "credential audit failure is not reported as success");
        await sql("DROP TRIGGER communications.RejectCredentialAudit");
        check(await Version(a1) == 2 && Convert.ToInt64(await scalar($"SELECT CredentialGeneration FROM communications.TelegramConnections WHERE AccountId='{a1}'")) == 1, "credential payload generation and metadata roll back with audit failure");
        await InScope(async sp => {
            var db = sp.GetRequiredService<CommunicationsDbContext>(); var vault = sp.GetRequiredService<TelegramCredentialVault>(); var row = await db.TelegramConnections.AsNoTracking().SingleAsync(x => x.AccountId == a1);
            check(row.Ciphertext != secrets[0] && !row.Ciphertext!.Contains(secrets[0], StringComparison.Ordinal) && vault.Unprotect(row) == secrets[0], "SQL stores protected token with correct bound decrypt");
            var workspace = row.WorkspaceId; row.WorkspaceId = "ws_com1_b"; await Reject(() => Task.FromResult(vault.Unprotect(row)), "CREDENTIAL_UNAVAILABLE"); row.WorkspaceId = workspace;
            var account = row.AccountId; row.AccountId = a2; await Reject(() => Task.FromResult(vault.Unprotect(row)), "CREDENTIAL_UNAVAILABLE"); row.AccountId = account;
            var cipher = row.Ciphertext; row.Ciphertext = "corrupted"; await Reject(() => Task.FromResult(vault.Unprotect(row)), "CREDENTIAL_UNAVAILABLE"); row.Ciphertext = cipher;
            row.CredentialGeneration++; await Reject(() => Task.FromResult(vault.Unprotect(row)), "CREDENTIAL_UNAVAILABLE"); return 0;
        });
        CryptoEvidence(check);
        await Verify(a1); await Verify(a2);
        await InScope(async sp => { await sp.GetRequiredService<TelegramConnectionManagement>().VerifySyntheticAsync("ws_com1_b", b1); return 0; });
        check((await Request("GET", PathFor(a1) + "/connection")).Body.GetProperty("verificationState").GetString() == "SYNTHETIC_VERIFIED", "getMe binds account generation without live connected status");
        bots[secrets[0]].Error = 401; await Reject(() => Verify(a1), "REAUTH_REQUIRED");
        var failedVerification = (await Request("GET", PathFor(a1) + "/connection")).Body;
        check(failedVerification.GetProperty("verificationState").GetString() == "UNVERIFIED" && failedVerification.GetProperty("lastError").GetString() == "REAUTH_REQUIRED", "verification denial invalidates current authority with safe error metadata");
        bots[secrets[0]].Error = 0; await Verify(a1);
        var timeoutBot = bots[secrets[0]]; timeoutBot.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); timeoutBot.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Reject(() => Verify(a1), "TIMEOUT"); timeoutBot.Release.SetResult(); timeoutBot.Entered = null; timeoutBot.Release = null;
        check((await Request("GET", PathFor(a1) + "/connection")).Body.GetProperty("verificationState").GetString() == "UNVERIFIED", "verification timeout never creates connected or verified state"); await Verify(a1);
        var duplicate = await Create("Secure duplicate bot"); await Save(duplicate, secrets[0], 1); await Reject(() => Verify(duplicate), "BOT_IDENTITY_CONFLICT");
        var options = services.GetRequiredService<TelegramPollingOptions>(); options.Enabled = true;
        bots[secrets[0]].Updates = JsonSerializer.Serialize(new[] { new { update_id = 1, message = new { message_id = 1, date = 1792108800, chat = new { id = 4_100_000_001L, type = "private" }, from = new { id = 4_100_000_001L, is_bot = false }, text = "SYNTHETIC_SECURE_INBOUND" } } });
        check(await InScope(sp => sp.GetRequiredService<TelegramInboundCoordinator>().PollAsync("ws_com1_a", a1)) == "POLL_COMMITTED", "existing polling consumes protected resolved credential");
        check(Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.Messages WHERE AccountId='{a1}'")) == 1, "protected credential integration reaches Core");
        var oldVersion = await Version(a1); var rotation = await Task.WhenAll(Save(a1, secrets[3], oldVersion), Save(a1, secrets[3], oldVersion));
        check(rotation.Count(x => x.Status == 200) == 1 && rotation.Count(x => x.Status == 409) == 1, "concurrent rotations one winner one safe conflict");
        check(await Version(a2) == 2 && await Version(b1) == 2, "A1 rotation leaves A2 B1 unchanged");
        var oldSnapshot = await InScope(sp => sp.GetRequiredService<TelegramCredentialVault>().ResolveForVerificationAsync("ws_com1_a", a1, default));
        await Save(a1, secrets[3], await Version(a1));
        var mixedSnapshot = new TelegramCredential(oldSnapshot!.Value, true, await Version(a1), oldSnapshot.CredentialGeneration, oldSnapshot.CredentialId);
        await Reject(() => InScope(async sp => { await sp.GetRequiredService<TelegramConnectionManagement>().CompleteSyntheticVerificationAsync("ws_com1_a", a1, mixedSnapshot, 301, default); return 0; }), "CONNECTION_GENERATION_STALE");
        check(await InScope(sp => sp.GetRequiredService<TelegramInboundCoordinator>().PollAsync("ws_com1_a", a1)) == "MISSING_CREDENTIALS", "rotated unverified credential cannot poll");
        await Verify(a1); check(Convert.ToInt64(await scalar($"SELECT NextOffset FROM communications.TelegramPolling WHERE AccountId='{a1}'")) == 2, "same bot rotation retains checkpoint");
        bots[secrets[3]].Id = 999; await Save(a1, secrets[3], await Version(a1)); await Reject(() => Verify(a1), "BOT_REBIND_REQUIRED"); bots[secrets[3]].Id = 301; await Verify(a1);
        var delayed = bots[secrets[3]]; delayed.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); delayed.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var verification = Verify(a1); await delayed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); await Save(a1, secrets[3], await Version(a1)); delayed.Release.SetResult();
        await Reject(() => verification, "CONNECTION_GENERATION_STALE"); delayed.Entered = null; delayed.Release = null;
        await Verify(a1); delayed.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); delayed.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await sql($"UPDATE communications.TelegramPolling SET RetryAfter=NULL WHERE AccountId='{a1}'");
        var poll = InScope(sp => sp.GetRequiredService<TelegramInboundCoordinator>().PollAsync("ws_com1_a", a1)); await delayed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); var beforePoll = methods.Count(x => x == "getUpdates");
        await Save(a1, secrets[3], await Version(a1)); delayed.Release.SetResult();
        check(await poll == "CONNECTION_GENERATION_STALE" && methods.Count(x => x == "getUpdates") == beforePoll, "rotation during leased getMe prevents stale getUpdates"); delayed.Entered = null; delayed.Release = null;
        delayed.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); delayed.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateDisconnect = Verify(a1); await delayed.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Request("POST", PathFor(a1) + "/disconnect", new { expectedVersion = await Version(a1) }); delayed.Release.SetResult();
        await Reject(() => lateDisconnect, "CONNECTION_GENERATION_STALE"); delayed.Entered = null; delayed.Release = null;
        check((await Request("GET", PathFor(a1) + "/connection")).Body.GetProperty("configurationState").GetString() == "DISCONNECTED", "late verification cannot resurrect disconnected runtime");
        await Save(a1, secrets[3], await Version(a1));
        var history = Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.Messages WHERE AccountId='{a1}'"));
        check((await Request("POST", PathFor(a1) + "/disconnect", new { expectedVersion = await Version(a1) })).Status == 200, "Telegram disconnect is local-only authorized mutation");
        check((await Request("GET", PathFor(a1) + "/connection")).Body.GetProperty("configurationState").GetString() == "DISCONNECTED", "disconnect clears secret without claiming remote revocation");
        check(Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.Messages WHERE AccountId='{a1}'")) == history, "disconnect preserves history"); await Reject(() => Verify(a1), "CREDENTIAL_UNAVAILABLE");
        await Save(a1, secrets[3], await Version(a1));
        check((await Request("DELETE", PathFor(a2) + "/credentials", new { expectedVersion = await Version(a2) })).Status == 200, "local credential removal independent");
        check((await Request("GET", PathFor(a1) + "/connection")).Body.GetProperty("credentialPresent").GetBoolean(), "A2 removal preserves A1 credential");
        await Request("POST", PathFor(a1) + "/disable", new { expectedVersion = await Version(a1) });
        check(await InScope(async sp => await sp.GetRequiredService<TelegramCredentialVault>().ResolveAsync("ws_com1_a", a1, default)) is null, "disabled account cannot resolve polling credential");
        await Request("POST", PathFor(a1) + "/enable", new { expectedVersion = await Version(a1) });
        check((await Request("GET", PathFor(a1) + "/connection")).Body.GetProperty("verificationState").GetString() == "UNVERIFIED", "re-enable never creates verification");
        var raceVersion = await Version(a1);
        var disableRace = await Task.WhenAll(Save(a1, secrets[3], raceVersion), Request("POST", PathFor(a1) + "/disable", new { expectedVersion = raceVersion }));
        check(disableRace.Count(r => r.Status == 200) == 1 && disableRace.Count(r => r.Status == 409) == 1, "disable and rotation serialize with one expected-version winner");
        await Request("POST", PathFor(a1) + "/enable", new { expectedVersion = await Version(a1) });
        custody.SyntheticReady = false; check((await Save(a1, secrets[3], await Version(a1))).Status == 503, "security admission missing blocks token storage"); custody.SyntheticReady = true;
        check(Convert.ToInt32(await scalar("SELECT COUNT(*) FROM communications.ChannelMutations WHERE ResponseJson LIKE '%fixture-secure%'")) == 0, "audit replay responses contain no credential");
        check(methods.All(x => x is "getMe" or "getWebhookInfo" or "getUpdates") && methods.Count(x => x is "sendMessage" or "sendPhoto" or "sendDocument" or "sendChatAction") == 0, "all provider outbound methods including sendChatAction equal zero");
        options.Enabled = false; await sql($"UPDATE communications.Conversations SET Status='CLOSED' WHERE AccountId IN ('{a1}','{a2}','{b1}')");
    }

    private void CryptoEvidence(Action<bool, string> check)
    {
        var path = Path.Combine(root, "crypto-proof"); using var first = KeyServices(path); var manager = first.GetRequiredService<IKeyManager>();
        var old = manager.CreateNewKey(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(30));
        var purpose = new[] { "test-custody", "telegram", "workspace-a", "account-a", "generation-1" };
        var cipher = first.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose).Protect("fixture-crypto-value");
        using var restarted = KeyServices(path);
        check(restarted.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose).Unprotect(cipher) == "fixture-crypto-value", "persistent key ring decrypts after provider recreation across independent instances");
        bool Refuses(IDataProtector protector) { try { protector.Unprotect(cipher); return false; } catch (CryptographicException) { return true; } }
        check(Refuses(restarted.GetRequiredService<IDataProtectionProvider>().CreateProtector("wrong-purpose")), "wrong protection purpose fails closed");
        using var missing = KeyServices(Path.Combine(root, "missing-ring")); check(Refuses(missing.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose)), "missing mismatched key ring fails closed");
        manager.CreateNewKey(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(60)); using var rotated = KeyServices(path);
        var next = rotated.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose).Protect("fixture-crypto-next");
        check(rotated.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose).Unprotect(cipher) == "fixture-crypto-value" && rotated.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose).Unprotect(next) == "fixture-crypto-next", "key rotation preserves old and new decryptability");
        manager.RevokeKey(old.KeyId, "synthetic proof"); using var revoked = KeyServices(path);
        check(Refuses(revoked.GetRequiredService<IDataProtectionProvider>().CreateProtector(purpose)), "revoked key refuses old credential");
        check(Directory.GetFiles(path, "key-*.xml").All(file => File.ReadAllText(file).Contains("encryptedSecret", StringComparison.Ordinal)), "persisted key material encrypted at rest using task certificate");
    }
    public async ValueTask DisposeAsync()
    {
        await fake.DisposeAsync(); await keys.DisposeAsync(); certificate.Dispose(); var full = Path.GetFullPath(root);
        if (!full.StartsWith(artifactRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("unsafe task key cleanup");
        if (Directory.Exists(full)) Directory.Delete(full, true); Console.WriteLine("PASS task-owned protected test key rings removed");
    }
}
