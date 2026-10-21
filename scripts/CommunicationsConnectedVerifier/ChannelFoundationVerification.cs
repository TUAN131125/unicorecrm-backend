using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

internal static class ChannelFoundationVerification
{
    internal static async Task Run(HttpClient http, IReadOnlyDictionary<string, string> tokens,
        Func<string, Task> sql, Func<string, Task<object?>> scalar, Action<bool, string> check)
    {
        using var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = http.BaseAddress };
        const string path = "/communications/channel-accounts";
        async Task<(int Status, JsonElement Body)> Request(string method, string url, object? body = null,
            string? actor = "admin", string workspace = "ws_com1_a", string? key = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (actor is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[actor]);
            request.Headers.Add("X-Workspace-Id", workspace);
            request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
            request.Headers.Add("X-Correlation-Id", "channel-foundation-fixture");
            if (body is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
            }
            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            check(!text.Contains("SELECT", StringComparison.Ordinal) && !text.Contains("Exception", StringComparison.Ordinal), "channel errors do not leak SQL/exception text");
            if (response.IsSuccessStatusCode) check(response.Headers.CacheControl?.NoStore == true, "channel metadata response no-store");
            return ((int)response.StatusCode, JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text).RootElement.Clone());
        }
        var anonymous = await Request("GET", path, actor: null);
        check(anonymous.Status == 401, $"channels unauthenticated denied (actual {anonymous.Status})");
        check((await Request("POST", path, new { providerKey = "gmail", displayName = "Denied" }, "a")).Status == 403, "conversation-read actor cannot create channel");
        check((await Request("GET", path)).Status == 403, "Studio admin does not inherit channel manage");
        await sql("INSERT INTO access.RoleCapabilities VALUES ('role_admin','communications.channels.manage'),('role_foreign','communications.channels.manage')");
        var catalog = await Request("GET", "/communications/channel-providers");
        check(catalog.Status == 200 && catalog.Body.GetArrayLength() == 4
            && catalog.Body.EnumerateArray().All(p => p.GetProperty("metadataDraft").GetString() == "SUPPORTED"
                && p.GetProperty("liveConnection").GetString() == "NOT_IMPLEMENTED"
                && p.GetProperty("inbound").GetString() == "NOT_IMPLEMENTED" && p.GetProperty("outbound").GetString() == "NOT_IMPLEMENTED"), "backend catalog describes drafts only; no live adapters");
        var before = await scalar("SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM communications.Conversations");
        var messages = await scalar("SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM communications.Messages");
        var legacy = await Request("GET", path);
        check(legacy.Body.GetProperty("items").EnumerateArray().All(x => x.GetProperty("status").GetString() == "UNVERIFIED"
            && x.GetProperty("providerKey").ValueKind == JsonValueKind.Null), "migration keeps legacy account unverified without provider backfill");
        var c1 = await Request("POST", path, new { providerKey = "gmail", displayName = "Gmail CSKH" }, key: "channel-create-one");
        var c2 = await Request("POST", path, new { providerKey = "gmail", displayName = "Gmail Sales" });
        var c3 = await Request("POST", path, new { providerKey = "telegram", displayName = "Telegram Support" });
        var c4 = await Request("POST", path, new { providerKey = "telegram", displayName = "Telegram Sales" });
        check(new[] { c1, c2, c3, c4 }.All(x => x.Status == 201), "Workspace A creates two Gmail and two Telegram accounts");
        string Id(JsonElement value) => value.GetProperty("account").GetProperty("id").GetString()!;
        var id1 = Id(c1.Body); var id2 = Id(c2.Body);
        check(new[] { c1, c2, c3, c4 }.Select(x => Id(x.Body)).Distinct().Count() == 4, "same provider accounts have distinct stable IDs");
        check(new[] { c1, c2, c3, c4 }.All(x => x.Body.GetProperty("account").GetProperty("status").GetString() == "DRAFT"
            && x.Body.GetProperty("account").GetProperty("externalAccountKey").ValueKind == JsonValueKind.Null), "drafts have no verified external identity or fake connected status");
        var replay = await Request("POST", path, new { providerKey = "gmail", displayName = "Gmail CSKH" }, key: "channel-create-one");
        check(replay.Status == 200 && Id(replay.Body) == id1 && replay.Body.GetProperty("replayed").GetBoolean(), "create replay returns same account");
        check((await Request("POST", path, new { providerKey = "gmail", displayName = "Changed" }, key: "channel-create-one")).Status == 409, "same key changed payload rejected");
        var simultaneous = await Task.WhenAll(Request("POST", path, new { providerKey = "gmail", displayName = "Concurrent" }, key: "same-concurrent"),
            Request("POST", path, new { providerKey = "gmail", displayName = "Concurrent" }, key: "same-concurrent"));
        check(simultaneous.Count(x => x.Status == 201) == 1 && simultaneous.Count(x => x.Status == 200) == 1
            && Id(simultaneous[0].Body) == Id(simultaneous[1].Body), "concurrent create replay has one account and audit");
        var foreign = await Request("POST", path, new { providerKey = "gmail", displayName = "B Gmail" }, "a", "ws_com1_b", "channel-create-one");
        check(foreign.Status == 201 && Id(foreign.Body) != id1, "Workspace B same key/provider creates independent account");
        check((await Request("GET", path + "/" + id1, actor: "a", workspace: "ws_com1_b")).Status == 404, "foreign channel read denied without metadata");
        check((await Request("PATCH", path + "/" + id1, new { displayName = "Attack", expectedVersion = 1 }, "a", "ws_com1_b")).Status == 404, "foreign channel update denied");
        check((await Request("GET", path, workspace: "ws_com1_b")).Status == 403, "untrusted Workspace header membership rejected");
        var bList = await Request("GET", path, actor: "a", workspace: "ws_com1_b");
        check(bList.Body.GetProperty("items").EnumerateArray().All(x => x.GetProperty("id").GetString() != id1), "Workspace switch lists no A metadata");
        var renamed = await Request("PATCH", path + "/" + id1, new { displayName = "Gmail Support", expectedVersion = 1 });
        check(renamed.Status == 200 && renamed.Body.GetProperty("account").GetProperty("configurationVersion").GetInt32() == 2, "rename increments configuration version");
        check((await Request("GET", path + "/" + id2)).Body.GetProperty("displayName").GetString() == "Gmail Sales", "rename A1 preserves A2");
        var updates = await Task.WhenAll(Request("PATCH", path + "/" + id1, new { displayName = "Winner1", expectedVersion = 2 }),
            Request("PATCH", path + "/" + id1, new { displayName = "Winner2", expectedVersion = 2 }));
        check(updates.Count(x => x.Status == 200) == 1 && updates.Count(x => x.Status == 409) == 1, "concurrent same-version updates have one winner");
        check((await Request("POST", path + "/" + id1 + "/disable", new { expectedVersion = 3 })).Status == 200, "disable selected account");
        check((await Request("GET", path + "/" + id2)).Body.GetProperty("status").GetString() == "DRAFT", "disable A1 does not affect A2");
        check((await Request("POST", path + "/" + id1 + "/enable", new { expectedVersion = 4 })).Status == 200, "re-enable returns draft only");
        check((await Request("POST", path + "/" + id1 + "/disconnect", new { expectedVersion = 5 })).Status == 501, "disconnect does not pretend remote revocation");
        await sql("INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('channel_readonly_provider','ws_com1_a','role_admin','communications.channels','providerKey','ReadOnly'),('channel_readonly_status','ws_com1_a','role_admin','communications.channels','status','ReadOnly')");
        check((await Request("PATCH", path + "/" + id1, new { displayName = "FLS rename", expectedVersion = 5 })).Status == 200,
            "rename needs only displayName writable; immutable provider/status may be readonly");
        check((await Request("POST", path + "/" + id1 + "/disable", new { expectedVersion = 6 })).Status == 403,
            "status readonly prevents disable");
        await sql("DELETE FROM access.RoleFieldSecurity WHERE PolicyId='channel_readonly_status'; INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('channel_readonly_name','ws_com1_a','role_admin','communications.channels','displayName','ReadOnly')");
        check((await Request("POST", path + "/" + id1 + "/disable", new { expectedVersion = 6 })).Status == 200,
            "disable needs only status writable; provider/name may be readonly");
        await sql("DELETE FROM access.RoleFieldSecurity WHERE PolicyId IN ('channel_readonly_provider','channel_readonly_name')");
        check((await Request("POST", path, new { providerKey = "unknown", displayName = "Unknown" })).Status == 400, "unknown provider rejected");
        check((await Request("POST", path, new { providerKey = "gmail", displayName = "Injected", status = "CONNECTED" })).Status == 400, "client cannot set connected status");
        check((await Request("PATCH", path + "/" + id1, new { displayName = "Bad", expectedVersion = "string" })).Status == 400, "wrong version type is validation not 500");
        var filtered = await Request("GET", path + "?provider=telegram&status=DRAFT&limit=1");
        check(filtered.Status == 200 && filtered.Body.GetProperty("totalCount").GetInt32() == 2, "provider status filter count");
        var cursor = filtered.Body.GetProperty("nextCursor").GetString()!;
        var second = await Request("GET", path + "?provider=telegram&status=DRAFT&limit=1&cursor=" + Uri.EscapeDataString(cursor));
        check(second.Status == 200 && second.Body.GetProperty("items")[0].GetProperty("id").GetString() != filtered.Body.GetProperty("items")[0].GetProperty("id").GetString(), "cursor returns distinct stable next account");
        check((await Request("GET", path + "?provider=gmail&status=DRAFT&limit=1&cursor=" + Uri.EscapeDataString(cursor))).Status == 400, "cursor filter mismatch rejected");
        check((await Request("GET", path + "?provider=telegram&status=DRAFT&limit=1&cursor=" + Uri.EscapeDataString(cursor), actor: "a", workspace: "ws_com1_b")).Status == 400, "cursor Workspace scope mismatch rejected");
        await sql("UPDATE access.Roles SET Version=Version+1 WHERE RoleId='role_admin'; INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('channel_fls','ws_com1_a','role_admin','communications.channels','displayName','Hidden')");
        check((await Request("GET", path)).Status == 403, "channel FLS fails closed without leaking hidden metadata");
        await sql("DELETE FROM access.RoleFieldSecurity WHERE PolicyId='channel_fls'; UPDATE access.Roles SET Version=Version+1 WHERE RoleId='role_admin'; INSERT INTO access.RoleCapabilities VALUES ('role_admin','communications.queue.read')");
        check((await Request("GET", path + "?provider=telegram&status=DRAFT&limit=1&cursor=" + Uri.EscapeDataString(cursor))).Status == 400, "stale channel cursor authority rejected");
        await sql("DELETE FROM access.RoleCapabilities WHERE RoleId='role_admin' AND Capability='communications.channels.manage'");
        check((await Request("GET", path)).Status == 403, "revoked management authority rejected");
        await sql("INSERT INTO access.RoleCapabilities VALUES ('role_admin','communications.channels.manage')");
        var conversationId = await scalar("SELECT TOP(1) Id FROM communications.Conversations WHERE WorkspaceId='ws_com1_a'");
        check((await Request("GET", $"/communications/conversations/{conversationId}/messages")).Status == 403, "channel-only manager cannot read conversation bodies");
        check(Equals(before, await scalar("SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM communications.Conversations"))
            && Equals(messages, await scalar("SELECT CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM communications.Messages")), "channel operations preserve Core conversations/messages exactly");
        check(Convert.ToInt32(await scalar("SELECT COUNT(*) FROM communications.ChannelAccounts WHERE WorkspaceId='ws_com1_a' AND ProviderKey='gmail'")) == 3, "SQL permits multiple accounts per provider with no replacement");
        check(Convert.ToInt32(await scalar("SELECT COUNT(*) FROM communications.ChannelAccounts WHERE Status='CONNECTED'")) == 0, "no fake connected records or provider calls required");
        check(Convert.ToInt32(await scalar("SELECT COUNT(*) FROM sys.indexes i JOIN sys.index_columns c ON c.object_id=i.object_id AND c.index_id=i.index_id JOIN sys.columns n ON n.object_id=c.object_id AND n.column_id=c.column_id WHERE i.object_id=OBJECT_ID('communications.ChannelAccounts') AND i.is_unique=1 AND n.name IN ('ProviderKey','Status')")) == 0,
            "schema has no unique provider or active account hardcap");
        await sql("CREATE TRIGGER communications.RejectChannelAudit ON communications.ChannelMutations AFTER INSERT AS BEGIN THROW 51000, 'Synthetic channel audit failure', 1; END");
        check((await Request("POST", path, new { providerKey = "gmail", displayName = "Rollback" })).Status == 500, "audit failure not reported as success");
        await sql("DROP TRIGGER communications.RejectChannelAudit");
        check(Convert.ToInt32(await scalar("SELECT COUNT(*) FROM communications.ChannelAccounts WHERE DisplayName='Rollback'")) == 0, "account and audit rollback atomically");
    }
}
