using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Communications;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Persistence;
using UnicoreCRM.Communications.Infrastructure.Telegram;

internal static class TelegramInboundVerification
{
    private sealed class CapturedLogs : ILogger<TelegramPollingWorker>
    {
        internal readonly List<string> Lines = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format) => Lines.Add(format(state, error));
    }
    private sealed class Diagnostics : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> subscriptions = [];
        internal int HttpEvents;
        public void OnNext(DiagnosticListener value) { if (value.Name == "HttpHandlerDiagnosticListener") subscriptions.Add(value.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> value) => Interlocked.Increment(ref HttpEvents);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { foreach (var subscription in subscriptions) subscription.Dispose(); }
    }
    private sealed class Credentials : ITelegramCredentials
    {
        internal readonly ConcurrentDictionary<(string, Guid), string> Bindings = new();
        internal bool SimulateLive;
        public ValueTask<TelegramCredential?> ResolveAsync(string workspace, Guid account, CancellationToken ct) =>
            ValueTask.FromResult(Bindings.TryGetValue((workspace, account), out var value) ? new TelegramCredential(value, !SimulateLive) : null);
    }
    private sealed class Bot(long id)
    {
        internal long Id = id;
        internal string Updates = "[]";
        internal int Error;
        internal bool Webhook;
        internal bool Malformed;
        internal TaskCompletionSource? Hold;
        internal TaskCompletionSource? Entered;
        internal readonly ConcurrentQueue<long> Offsets = new();
    }

    internal static async Task<long> Run(string connection, HttpClient authenticatedHost,
        IReadOnlyDictionary<string, string> tokens, Func<string, Task> sql, Func<string, Task<object?>> scalar,
        Action<bool, string> check)
    {
        var bots = new ConcurrentDictionary<string, Bot>();
        var requests = new ConcurrentQueue<string>();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var fake = builder.Build();
        fake.MapPost("/bot{key}/{method}", async (string key, string method, Microsoft.AspNetCore.Http.HttpContext context) =>
        {
            requests.Enqueue(method); // store method names only, never token URLs/payloads
            if (!bots.TryGetValue(key, out var bot)) { context.Response.StatusCode = 401; return; }
            if (method == "getMe") { await context.Response.WriteAsJsonAsync(new { ok = true, result = new { id = bot.Id, is_bot = true } }); return; }
            if (method == "getWebhookInfo") { await context.Response.WriteAsJsonAsync(new { ok = true, result = new { url = bot.Webhook ? "https://example.invalid/hook" : "" } }); return; }
            if (method != "getUpdates") { context.Response.StatusCode = 405; return; }
            using var body = await JsonDocument.ParseAsync(context.Request.Body);
            var offset = body.RootElement.GetProperty("offset").GetInt64();
            bot.Offsets.Enqueue(offset);
            bot.Entered?.TrySetResult();
            if (bot.Hold is { } hold) await hold.Task.WaitAsync(context.RequestAborted);
            if (bot.Error > 0)
            {
                context.Response.StatusCode = bot.Error;
                await context.Response.WriteAsJsonAsync(new { ok = false, error_code = bot.Error, parameters = new { retry_after = 11 } }); return;
            }
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(bot.Malformed ? "{broken" : "{\"ok\":true,\"result\":" + bot.Updates + "}");
        });
        await fake.StartAsync();
        var address = fake.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var credentials = new Credentials();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCommunicationsModule(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:UnicoreCRM"] = connection }).Build());
        services.AddSingleton<ITelegramCredentials>(credentials);
        services.AddSingleton(new TelegramProviderAdmission(true, true));
        services.AddHttpClient<TelegramApiClient>(client => client.BaseAddress = new Uri(address));
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<TelegramPollingOptions>();
        options.Enabled = true;
        TelegramPollingWorker Worker() => new(provider.GetRequiredService<IServiceScopeFactory>(), options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TelegramPollingWorker>.Instance);
        async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
        {
            await using var scope = provider.CreateAsyncScope();
            return await action(scope.ServiceProvider);
        }
        async Task<Guid> Account(string workspace, string name, long botId)
        {
            var id = await InScope(async sp => {
                var db = sp.GetRequiredService<CommunicationsDbContext>();
                var account = ChannelAccount.Draft(workspace, "telegram", "MESSAGING", name, DateTimeOffset.UtcNow);
                db.Accounts.Add(account); await db.SaveChangesAsync(); return account.Id;
            });
            bots[name] = new(botId); credentials.Bindings[(workspace, id)] = name;
            return id;
        }
        async Task Admit(string workspace, Guid id, long botId) => await InScope(async sp => {
            await sp.GetRequiredService<TelegramInboundCoordinator>().AdmitSyntheticAsync(workspace, id, botId); return 0;
        });
        Task<string> Poll(string workspace, Guid id, CancellationToken ct = default) => InScope(sp => sp.GetRequiredService<TelegramInboundCoordinator>().PollAsync(workspace, id, ct));
        Task Due(Guid id) => sql($"UPDATE communications.TelegramPolling SET Active=1,RetryAfter=NULL,LeaseExpiry=NULL,LeaseOwner=NULL WHERE AccountId='{id}'");
        async Task<long> Count(Guid id) => Convert.ToInt64(await scalar($"SELECT COUNT(*) FROM communications.Messages WHERE AccountId='{id}'"));
        async Task<long> Offset(Guid id) => Convert.ToInt64(await scalar($"SELECT NextOffset FROM communications.TelegramPolling WHERE AccountId='{id}'"));
        async Task Reject(Func<Task> action, string code)
        {
            try { await action(); throw new InvalidOperationException("expected " + code); }
            catch (TelegramFailure error) { check(error.Code == code, "Telegram rejects " + code); }
        }
        async Task<(int Status, string Body)> Request(string path, string actor = "a", string workspace = "ws_com1_a", object? payload = null)
        {
            using var request = new HttpRequestMessage(payload is null ? HttpMethod.Get : HttpMethod.Post, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[actor]);
            request.Headers.Add("X-Workspace-Id", workspace); request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
            if (payload is not null) request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await authenticatedHost.SendAsync(request);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        var a1 = await Account("ws_com1_a", "fixture-aone", 101);
        var a2 = await Account("ws_com1_a", "fixture-atwo", 102);
        var b1 = await Account("ws_com1_b", "fixture-bone", 201);
        var a = bots["fixture-aone"]; var aOther = bots["fixture-atwo"]; var b = bots["fixture-bone"];
        check(await Poll("ws_com1_a", a1) == "ACCOUNT_NOT_DUE" && requests.IsEmpty, "unverified draft does not call Telegram");
        options.Enabled = false;
        check(await Poll("ws_com1_a", a1) == "FEATURE_DISABLED" && requests.IsEmpty, "disabled feature performs no provider request");
        options.Enabled = true;
        check(await Poll("wrong-workspace", a1) == "MISSING_CREDENTIALS" && requests.IsEmpty, "wrong Workspace cannot resolve account credentials");
        check(await new MissingTelegramCredentials().ResolveAsync("ws_com1_a", a1, default) is null, "production credential port remains unavailable");
        credentials.SimulateLive = true;
        check(await Poll("ws_com1_a", a1) == "LIVE_NOT_ADMITTED" && requests.IsEmpty, "live credentials cannot activate this synthetic-only runtime");
        credentials.SimulateLive = false;
        await Admit("ws_com1_a", a1, 101); await Admit("ws_com1_a", a2, 102); await Admit("ws_com1_b", b1, 201);
        var collision = await Account("ws_com1_b", "fixture-duplicate", 101);
        await Reject(() => Admit("ws_com1_b", collision, 101), "BOT_IDENTITY_CONFLICT");
        await Reject(() => Admit("ws_com1_b", collision, 999), "BOT_BINDING_INVALID");
        credentials.Bindings.TryRemove(("ws_com1_b", collision), out _);
        a.Updates = Batch(Text(10, 1, "SYNTHETIC_TELEGRAM_A1"), Text(10, 1, "SYNTHETIC_TELEGRAM_A1"));
        aOther.Updates = Batch(Text(10, 1, "SYNTHETIC_TELEGRAM_A2"));
        b.Updates = Batch(Text(70, 1, "SYNTHETIC_TELEGRAM_B1"));
        await Worker().TickAsync(default);
        check(await Count(a1) == 1 && await Count(a2) == 1 && await Count(b1) == 1, "A1 A2 B1 worker commits three independent account messages including same chat IDs");
        check(await Offset(a1) == 11 && await Offset(a2) == 11 && await Offset(b1) == 71, "per-account durable offsets are independent and duplicate batch is idempotent");
        check(Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.Conversations WHERE AccountId IN ('{a1}','{a2}','{b1}') AND AssigneeMemberId IS NULL")) == 3, "inbound creates unassigned NEW conversations without CRM linking");
        var conversation = Convert.ToInt64(await scalar($"SELECT Id FROM communications.Conversations WHERE AccountId='{a1}'"));
        var foreignConversation = Convert.ToInt64(await scalar($"SELECT Id FROM communications.Conversations WHERE AccountId='{b1}'"));
        check((await Request($"/communications/conversations/{conversation}/messages")).Status == 404, "unassigned Telegram protected content is unavailable");
        await sql("IF NOT EXISTS(SELECT 1 FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability='communications.claim') INSERT INTO access.RoleCapabilities VALUES ('role_a','communications.claim')");
        check((await Request($"/communications/conversations/{conversation}/claim", payload: new { })).Status == 400, "Claim still rejects an injected body");
        using (var request = new HttpRequestMessage(HttpMethod.Post, $"/communications/conversations/{conversation}/claim"))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens["a"]);
            request.Headers.Add("X-Workspace-Id", "ws_com1_a"); request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
            using var response = await authenticatedHost.SendAsync(request);
            check((int)response.StatusCode == 200, "actual Claim authorizes Telegram conversation through existing handler");
        }
        check((await Request($"/communications/conversations/{conversation}/messages")).Body.Contains("SYNTHETIC_TELEGRAM_A1", StringComparison.Ordinal), "assigned actor reads Telegram text via actual protected API");
        check((await Request($"/communications/conversations/{conversation}/messages", "b")).Status == 404, "other member cannot read Telegram content");
        check((await Request($"/communications/conversations/{foreignConversation}/messages")).Status == 404, "Workspace A cannot read Workspace B Telegram data");
        check(!(await Request("/communications/conversations?limit=100")).Body.Contains("SYNTHETIC_TELEGRAM_B1", StringComparison.Ordinal), "Workspace A Inbox does not return B message text");
        await sql("INSERT INTO access.RoleCapabilities VALUES ('role_a','communications.link')");
        var linkVersion = Convert.ToInt64(await scalar($"SELECT Version FROM communications.Conversations WHERE Id={conversation}"));
        check((await Request($"/communications/conversations/{conversation}/crm-link", payload: new { targetType = "CONTACT", targetId = "contact_crm_one", expectedVersion = linkVersion })).Status == 200,
            "Telegram conversation CRM link requires an explicit existing authorized manual command");

        // Legitimate existing lifecycle + link state are preserved by new inbound and duplicate replay.
        await sql($"UPDATE communications.Conversations SET Status='CLOSED',Version=Version+1 WHERE Id={conversation}");
        var version = Convert.ToInt64(await scalar($"SELECT Version FROM communications.Conversations WHERE Id={conversation}"));
        await Due(a1); check(await Poll("ws_com1_a", a1) == "POLL_COMMITTED", "restart replay returns durable admission");
        check(Convert.ToInt64(await scalar($"SELECT Version FROM communications.Conversations WHERE Id={conversation} AND Status='CLOSED'")) == version, "old update replay does not reopen CLOSED or increment version");
        a.Updates = Batch(Text(15, 2, "SYNTHETIC_NEW_TEXT")); await Due(a1); await Poll("ws_com1_a", a1);
        check(Convert.ToInt64(await scalar($"SELECT Version FROM communications.Conversations WHERE Id={conversation} AND Status='OPEN' AND AssigneeMemberId='member_a'")) == version + 1, "new text reopens same CLOSED conversation once and preserves assignee");
        check(Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.AuditRecords WHERE ConversationId={conversation} AND Operation='CONVERSATION_AUTO_REOPENED'")) == 1, "Telegram uses Core auto-reopen audit");
        check(Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.ConversationRecordLinks WHERE ConversationId={conversation} AND TargetId='contact_crm_one' AND UnlinkedAt IS NULL")) == 1,
            "new Telegram inbound preserves the existing manual CRM link");
        await sql($"UPDATE communications.Conversations SET Status='PENDING',Version=Version+1 WHERE Id={conversation}");
        a.Updates = Batch(Text(16, 3, "SYNTHETIC_PENDING_TEXT")); await Due(a1); await Poll("ws_com1_a", a1);
        check((string)(await scalar($"SELECT Status FROM communications.Conversations WHERE Id={conversation}"))! == "OPEN", "PENDING reopens through same Core append boundary");
        a.Updates = Batch(Text(17, 3, "SYNTHETIC_PENDING_TEXT")); await Due(a1); await Poll("ws_com1_a", a1);
        check(await Count(a1) == 3 && await Offset(a1) == 18, "different update same provider message dedups in Core and advances admission safely");

        // SQL fault after business writes but before checkpoint/admission commit: everything rolls back.
        a.Updates = Batch(Text(20, 4, "SYNTHETIC_ROLLBACK_TEXT"));
        await sql("CREATE TRIGGER communications.TelegramFixtureFailure ON communications.TelegramAdmissions AFTER INSERT AS BEGIN THROW 51000,'Synthetic transaction failure',1; END");
        await Due(a1); check(await Poll("ws_com1_a", a1) == "STORAGE_TRANSIENT", "SQL fault before checkpoint is safely classified");
        check(await Count(a1) == 3 && await Offset(a1) == 18, "crash-equivalent SQL fault after Message insert rolls back business admission checkpoint");
        await sql("DROP TRIGGER communications.TelegramFixtureFailure");
        await Due(a1); await Poll("ws_com1_a", a1);
        check(await Count(a1) == 4 && await Offset(a1) == 21 && a.Offsets.Last() == 18, "retry starts at last durable offset and commits once");
        await Due(a1); await Poll("ws_com1_a", a1);
        check(await Count(a1) == 4 && a.Offsets.Last() == 21, "post-business-and-checkpoint crash replay uses committed offset without duplication");

        a.Updates = Batch("{\"update_id\":30,\"callback_query\":{}}", Text(31, 5, "/start"),
            "{\"update_id\":32,\"message\":{\"chat\":{\"type\":\"group\"}}}",
            "{\"update_id\":33,\"message\":{\"chat\":{\"type\":\"private\"},\"photo\":[]}}");
        await Due(a1); await Poll("ws_com1_a", a1);
        check(await Count(a1) == 4 && await Offset(a1) == 34, "unsupported update group media and start get durable classification without fake CRM messages");
        check(Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.TelegramAdmissions WHERE AccountId='{a1}' AND Classification='CONTROL_START'")) == 1, "start classified without automatic reply or CRM identity creation");
        foreach (var error in new[] { 401, 409, 429, 503 })
        {
            a.Error = error; await Due(a1);
            var result = await Poll("ws_com1_a", a1);
            check(result == (error switch { 401 => "REAUTH_REQUIRED", 409 => "POLLING_CONFLICT", 429 => "RATE_LIMITED", _ => "PROVIDER_TRANSIENT" }), "provider error safely classified " + error);
            check(await Offset(a1) == 34, "provider failure never advances offset " + error);
            if (error == 429) check(Convert.ToInt32(await scalar($"SELECT DATEDIFF(second,SYSDATETIMEOFFSET(),RetryAfter) FROM communications.TelegramPolling WHERE AccountId='{a1}'")) >= 9, "429 provider retry_after persisted per account");
        }
        a.Error = 503; aOther.Updates = Batch(Text(40, 2, "SYNTHETIC_A2_CONTINUES")); b.Updates = Batch(Text(80, 2, "SYNTHETIC_B1_CONTINUES"));
        await Due(a1); await Due(a2); await Due(b1); await Worker().TickAsync(default);
        check(await Count(a2) == 2 && await Count(b1) == 2 && await Offset(a1) == 34, "A1 provider error does not block A2 B1 scheduler progress");
        a.Error = 0; a.Malformed = true; await Due(a1);
        check(await Poll("ws_com1_a", a1) == "RESPONSE_INVALID" && await Offset(a1) == 34, "malformed provider response does not ack");
        a.Malformed = false; a.Webhook = true; await Due(a1); var pollsBefore = a.Offsets.Count;
        check(await Poll("ws_com1_a", a1) == "WEBHOOK_CONFLICT" && a.Offsets.Count == pollsBefore, "webhook conflict stops before getUpdates without deleting webhook");
        a.Webhook = false; a.Id = 777; await Due(a1);
        check(await Poll("ws_com1_a", a1) == "BOT_BINDING_INVALID" && a.Offsets.Count == pollsBefore, "changed external bot identity cannot consume account updates");
        a.Id = 101;
        a.Updates = Batch(Text(50, 6, "SYNTHETIC_AFTER_MALFORMED"), "{\"update_id\":49}"); await Due(a1);
        check(await Poll("ws_com1_a", a1) == "UPDATE_ORDER_INVALID" && await Offset(a1) == 34, "out-of-order batch fails before committing a higher acknowledgement");

        // Real two-scope race holds an HTTP poll while another coordinator attempts acquisition.
        a.Updates = "[]"; a.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        a.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); await Due(a1);
        var inFlight = Poll("ws_com1_a", a1);
        await a.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        check(await Poll("ws_com1_a", a1) == "LEASE_BUSY", "second process-equivalent coordinator cannot overlap provider poll");
        await sql($"UPDATE communications.TelegramPolling SET LeaseExpiry=DATEADD(second,-1,SYSDATETIMEOFFSET()) WHERE AccountId='{a1}'");
        check(await Poll("ws_com1_a", a1) == "LEASE_BUSY", "session ownership prevents overlap even when durable lease expires");
        aOther.Updates = Batch(Text(55, 22, "SYNTHETIC_A2_DURING_LONG_POLL")); b.Updates = Batch(Text(85, 22, "SYNTHETIC_B1_DURING_LONG_POLL"));
        await Due(a2); await Due(b1); await Worker().TickAsync(default);
        check(await Count(a2) == 3 && await Count(b1) == 3, "held A1 long poll does not block worker slots for A2 B1");
        a.Hold.SetResult(); a.Hold = null; await inFlight;
        await Due(a1); a.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var canceled = new CancellationTokenSource(200))
        {
            try { await Poll("ws_com1_a", a1, canceled.Token); throw new InvalidOperationException("expected cancellation"); }
            catch (OperationCanceledException) { check(true, "poll cancellation stops HTTP and never acknowledges updates"); }
        }
        a.Hold.SetResult(); a.Hold = null;
        check(await Offset(a1) == 34, "canceled request preserves last committed checkpoint");
        // Timeout exercises client deadline, not merely host cancellation.
        await Due(a1); a.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        check(await Poll("ws_com1_a", a1) == "TIMEOUT", "bounded Telegram timeout safely classified");
        a.Hold.SetResult(); a.Hold = null;

        await sql($"UPDATE communications.ChannelAccounts SET Status='DISABLED',ConfigurationVersion=ConfigurationVersion+1 WHERE Id='{a1}'");
        await Due(a1); pollsBefore = a.Offsets.Count;
        check(await Poll("ws_com1_a", a1) == "ACCOUNT_NOT_DUE" && a.Offsets.Count == pollsBefore, "disabled account performs zero getUpdates");
        aOther.Updates = Batch(Text(60, 3, "SYNTHETIC_A2_AFTER_DISABLE")); b.Updates = Batch(Text(90, 3, "SYNTHETIC_B1_AFTER_DISABLE"));
        await Due(a2); await Due(b1); await Worker().TickAsync(default);
        check(await Count(a2) == 4 && await Count(b1) == 4, "disable A1 leaves A2 B1 running independently");
        await sql($"UPDATE communications.ChannelAccounts SET Status='DRAFT' WHERE Id='{a1}'");
        check(await Poll("ws_com1_a", a1) == "ACCOUNT_NOT_DUE", "stale connection generation cannot resume after metadata change");
        await sql($"UPDATE communications.ChannelAccounts SET ConfigurationVersion=ConfigurationVersion-1 WHERE Id='{a1}'");
        await Due(a1);
        // Durable fenced ownership rechecks reject both stale owner and stale generation before Core writes.
        var leaseOwner = Guid.NewGuid();
        await sql($"UPDATE communications.TelegramPolling SET Fence=Fence+1,LeaseOwner='{leaseOwner}',LeaseExpiry=DATEADD(second,60,SYSDATETIMEOFFSET()) WHERE AccountId='{a1}'");
        var fence = Convert.ToInt64(await scalar($"SELECT Fence FROM communications.TelegramPolling WHERE AccountId='{a1}'"));
        await Reject(() => InScope(async sp => { await sp.GetRequiredService<TelegramInboundCoordinator>().CommitAsync("ws_com1_a", a1, 1, Guid.NewGuid(), fence - 1, new(100, "UNSUPPORTED_UPDATE"), default); return 0; }), "LEASE_LOST");
        await Reject(() => InScope(async sp => { await sp.GetRequiredService<TelegramInboundCoordinator>().CommitAsync("ws_com1_a", a1, 2, leaseOwner, fence, new(100, "UNSUPPORTED_UPDATE"), default); return 0; }), "LEASE_LOST");
        await Reject(() => InScope(async sp => { await sp.GetRequiredService<TelegramInboundCoordinator>().CommitAsync("ws_com1_b", a1, 1, leaseOwner, fence, new(100, "UNSUPPORTED_UPDATE"), default); return 0; }), "LEASE_LOST");
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => InScope(async sp => {
            await sp.GetRequiredService<TelegramInboundCoordinator>().CommitAsync("ws_com1_a", a1, 1, leaseOwner, fence, new(100, "UNSUPPORTED_UPDATE"), default); return 0;
        })));
        check(Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.TelegramAdmissions WHERE AccountId='{a1}' AND UpdateId=100")) == 1 && await Offset(a1) == 101,
            "concurrent same update admission commits exactly one durable result and checkpoint");
        await Due(a1); a.Updates = "[]"; await Poll("ws_com1_a", a1);
        check(await Offset(a1) == 101, "restart reacquires durable lease and restores account checkpoint");
        await sql($"UPDATE communications.TelegramPolling SET AdmittedAt=DATEADD(hour,-25,SYSDATETIMEOFFSET()),LastSuccessfulPollAt=NULL,RetryAfter='9999-12-31' WHERE AccountId='{a1}'");
        var logs = new CapturedLogs();
        var warningWorker = new TelegramPollingWorker(provider.GetRequiredService<IServiceScopeFactory>(), options, logs);
        await warningWorker.TickAsync(default); await warningWorker.TickAsync(default);
        check(logs.Lines.Count(x => x.Contains("retention risk", StringComparison.Ordinal)) == 1, "never-successful terminal-blocked account warns independently of due polling with bounded cadence");

        var httpActivities = 0;
        using var diagnostics = new Diagnostics();
        using var diagnosticSubscription = DiagnosticListener.AllListeners.Subscribe(diagnostics);
        using var listener = new ActivityListener { ShouldListenTo = source => source.Name == "System.Net.Http",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => Interlocked.Increment(ref httpActivities) };
        ActivitySource.AddActivityListener(listener);
        await Due(a1); await Poll("ws_com1_a", a1);
        check(httpActivities == 0, "Telegram typed transport suppresses HTTP activities even with an active subscriber");
        check(diagnostics.HttpEvents == 0, "Telegram transport emits no token-bearing HTTP DiagnosticListener request event");
        check(new TelegramCredential("synthetic-marker").ToString() == "[REDACTED]", "credential accidental string formatting is redacted");
        check(requests.All(x => x is "getMe" or "getWebhookInfo" or "getUpdates"), "fake provider records only approved read-side methods");
        check(requests.Count(x => x is "sendMessage" or "sendPhoto" or "sendDocument") == 0, "sendMessage sendPhoto sendDocument and all other outbound methods equal zero");
        check(Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.ChannelAccounts WHERE Id IN ('{a1}','{a2}','{b1}') AND Status='DRAFT'")) == 3, "synthetic admission never marks Studio draft accounts connected");
        // Retire only this fixture's extra queue entries/grants so existing browser assertions retain their original setup.
        await sql($"UPDATE communications.Conversations SET Status='CLOSED',Version=Version+1 WHERE AccountId IN ('{a2}','{b1}'); DELETE FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability IN ('communications.claim','communications.link')");
        await fake.StopAsync();
        return conversation;
    }

    private static string Batch(params string[] updates) => "[" + string.Join(',', updates) + "]";
    private static string Text(long update, long message, string text) => JsonSerializer.Serialize(new { update_id = update,
        message = new { message_id = message, date = 1792108800L, chat = new { id = 4_000_000_001L, type = "private" },
            from = new { id = 4_000_000_001L, is_bot = false }, text } });
}
