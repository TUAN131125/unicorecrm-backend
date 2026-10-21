using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Communications.Application;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Persistence;

internal static class LifecycleVerification
{
    internal static async Task Run(IServiceProvider services, HttpClient http, IReadOnlyDictionary<string, string> tokens,
        Func<string, Task> execute, Func<string, Task<object?>> scalar, Action<bool, string> check)
    {
        var ids = new List<long>();
        var at = DateTimeOffset.UtcNow.AddDays(-20);
        async Task<(long Id, long Version, Guid Participant)> Fixture(string status, string? assignee = "member_a")
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>();
            var account = await db.Accounts.SingleAsync(x => x.WorkspaceId == "ws_com1_a");
            var participant = await db.Participants.SingleAsync(x => x.AccountId == account.Id);
            var row = new Conversation(account, "lifecycle-" + Guid.NewGuid().ToString("N"), at, assignee);
            db.Add(row); await db.SaveChangesAsync();
            db.Messages.Add(row.Append(participant, "initial-" + Guid.NewGuid().ToString("N"), "SYNTHETIC_LIFECYCLE_BODY", at));
            if (status != "NEW") row.Transition("OPEN");
            if (status is "PENDING" or "CLOSED") row.Transition(status);
            await db.SaveChangesAsync(); ids.Add(row.Id);
            return (row.Id, row.Version, participant.Id);
        }
        async Task<(HttpStatusCode Status, JsonElement Body)> Transition(long id, string target, long version,
            string? actor = "a", string workspace = "ws_com1_a", string? raw = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/communications/conversations/{id}/transition");
            if (actor is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[actor]);
            request.Headers.Add("X-Workspace-Id", workspace); request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
            request.Headers.Add("X-Correlation-Id", "lifecycle-correlation-fixture");
            request.Content = new StringContent(raw ?? JsonSerializer.Serialize(new { targetStatus = target, expectedVersion = version }), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            check(!body.Contains("SYNTHETIC_") && !body.Contains("SELECT") && !body.Contains("Exception"), "lifecycle response has no protected content or diagnostics");
            if (response.IsSuccessStatusCode) check(response.Headers.CacheControl?.NoStore == true, "transition forbids HTTP cache");
            return (response.StatusCode, body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone());
        }
        async Task<(HttpStatusCode Status, JsonElement Body)> Get(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens["a"]);
            request.Headers.Add("X-Workspace-Id", "ws_com1_a"); request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
            using var response = await http.SendAsync(request);
            return (response.StatusCode, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone());
        }
        Task<object?> Value(long id, string column) => scalar($"SELECT {column} FROM communications.Conversations WHERE Id={id}");
        Task<object?> Audits(long id) => scalar($"SELECT COUNT(*) FROM communications.AuditRecords WHERE ConversationId={id}");
        await execute("INSERT INTO access.RoleCapabilities VALUES ('role_a','communications.transition'),('role_b','communications.transition')");
        var states = new[] { "NEW", "OPEN", "PENDING", "CLOSED" };
        var allowed = new HashSet<(string, string)> { ("NEW", "OPEN"), ("NEW", "CLOSED"), ("OPEN", "PENDING"),
            ("OPEN", "CLOSED"), ("PENDING", "OPEN"), ("PENDING", "CLOSED"), ("CLOSED", "OPEN") };
        foreach (var from in states)
        foreach (var to in states)
        {
            var row = await Fixture(from);
            var result = await Transition(row.Id, to, row.Version);
            var changed = allowed.Contains((from, to));
            check(result.Status == (changed || from == to ? HttpStatusCode.OK : HttpStatusCode.Conflict), $"HTTP transition {from} -> {to}");
            check(Convert.ToInt64(await Value(row.Id, "Version")) == row.Version + (changed ? 1 : 0)
                && Convert.ToInt32(await Audits(row.Id)) == (changed ? 1 : 0), "transition or no-op has exact version/audit delta");
            check((string?)await Value(row.Id, "Status") == (changed ? to : from)
                && (DateTimeOffset)(await Value(row.Id, "LastActivityAt"))! == at, "state persisted; human transition preserves message recency");
            if (changed)
                check(Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.AuditRecords WHERE ConversationId={row.Id} AND Operation='CONVERSATION_TRANSITIONED' AND PreviousStatus='{from}' AND NewStatus='{to}' AND ActorMemberId='member_a' AND CorrelationId='lifecycle-correlation-fixture' AND MessageId IS NULL")) == 1,
                    "transition audit records previous/new state actor and correlation");
            if (from == to) check(!result.Body.GetProperty("changed").GetBoolean()
                && (!result.Body.TryGetProperty("auditId", out var noOpAudit) || noOpAudit.ValueKind == JsonValueKind.Null), "same-state current-version no-op asserts no audit");
            check((await Transition(row.Id, changed ? to : from, row.Version - 1)).Status == HttpStatusCode.Conflict, "stale version conflicts even when target matches");
        }
        var secured = await Fixture("NEW");
        check((await Transition(secured.Id, "OPEN", secured.Version, null)).Status == HttpStatusCode.Unauthorized, "anonymous transition denied");
        foreach (var actor in new[] { "b", "admin", "manager", "triage" })
            check((await Transition(secured.Id, "OPEN", secured.Version, actor)).Status == (actor == "b" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden), "other assignee and default/elevated role transition denied");
        check((await Transition(secured.Id, "OPEN", secured.Version, workspace: "ws_com1_b")).Status == HttpStatusCode.Forbidden, "transition requires capability in trusted target Workspace");
        check((await Transition(secured.Id, "OPEN", secured.Version, "b", "ws_com1_b")).Status == HttpStatusCode.Forbidden, "untrusted Workspace transition denied");
        await execute("INSERT INTO access.RoleCapabilities VALUES ('role_foreign','communications.transition')");
        check((await Transition(secured.Id, "OPEN", secured.Version, workspace: "ws_com1_b")).Status == HttpStatusCode.NotFound, "foreign record hidden even with target Workspace transition grant");
        await execute("DELETE FROM access.RoleCapabilities WHERE RoleId='role_foreign' AND Capability='communications.transition'");
        var unassigned = await Fixture("NEW", null);
        check((await Transition(unassigned.Id, "OPEN", unassigned.Version)).Status == HttpStatusCode.NotFound, "unassigned Queue never grants transition");
        await execute("DELETE FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability='communications.transition'");
        check((await Transition(secured.Id, "OPEN", secured.Version)).Status == HttpStatusCode.Forbidden, "current capability revocation enforced with same JWT");
        await execute("INSERT INTO access.RoleCapabilities VALUES ('role_a','communications.transition'); DELETE FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability='communications.read'");
        check((await Transition(secured.Id, "OPEN", secured.Version)).Status == HttpStatusCode.Forbidden, "transition capability without read denied");
        await execute("INSERT INTO access.RoleCapabilities VALUES ('role_a','communications.read'); UPDATE workspace.Memberships SET Status='Suspended' WHERE MembershipId='membership_a'");
        check((await Transition(secured.Id, "OPEN", secured.Version)).Status == HttpStatusCode.Forbidden, "inactive membership transition denied");
        await execute("UPDATE workspace.Memberships SET Status='Active' WHERE MembershipId='membership_a'");
        foreach (var fieldAccess in new[] { "ReadOnly", "Hidden" })
        {
            await execute($"INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('status_fls','ws_com1_a','role_a','communications','status','{fieldAccess}')");
            check((await Transition(secured.Id, "OPEN", secured.Version)).Status == HttpStatusCode.Forbidden, "status write FLS enforced");
            if (fieldAccess == "Hidden") check((await Get($"/communications/conversations/{secured.Id}")).Status == HttpStatusCode.Forbidden, "required status read FLS fails closed");
            await execute("DELETE FROM access.RoleFieldSecurity WHERE PolicyId='status_fls'");
        }
        await execute("UPDATE access.RoleDataScopes SET Scope='Team' WHERE PolicyId='scope_a'");
        check((await Transition(secured.Id, "OPEN", secured.Version)).Status == HttpStatusCode.Forbidden, "unsupported transition record scope denied");
        await execute("UPDATE access.RoleDataScopes SET Scope='Workspace' WHERE PolicyId='scope_a'");
        foreach (var invalid in new[] { "{}", "[]", "{", "{\"targetStatus\":\"OPEN\",\"expectedVersion\":\"1\"}",
            "{\"targetStatus\":\"open\",\"expectedVersion\":1}", "{\"targetStatus\":\"OPEN\",\"expectedVersion\":-1}",
            "{\"targetStatus\":\"OPEN\",\"expectedVersion\":1,\"workspaceId\":\"ws_com1_a\"}",
            "{\"targetStatus\":\"OPEN\",\"targetStatus\":\"CLOSED\"}", new string(' ', 4097) })
            check((await Transition(secured.Id, "OPEN", secured.Version, raw: invalid)).Status == HttpStatusCode.BadRequest, "strict JSON/version/authority validation returns 400");
        var race = await Task.WhenAll(Transition(secured.Id, "OPEN", secured.Version), Transition(secured.Id, "CLOSED", secured.Version));
        check(race.Count(x => x.Status == HttpStatusCode.OK) == 1 && race.Count(x => x.Status == HttpStatusCode.Conflict) == 1
            && Convert.ToInt32(await Audits(secured.Id)) == 1, "concurrent transitions yield one commit audit and one 409");
        var rollback = await Fixture("OPEN");
        await execute("CREATE TRIGGER communications.RejectLifecycleAudit ON communications.AuditRecords AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Operation='CONVERSATION_TRANSITIONED') THROW 51002, 'Fixture lifecycle audit rejected', 1; END");
        check((await Transition(rollback.Id, "CLOSED", rollback.Version)).Status == HttpStatusCode.InternalServerError, "audit storage failure not reported as transition success");
        await execute("DROP TRIGGER communications.RejectLifecycleAudit");
        check((string?)await Value(rollback.Id, "Status") == "OPEN" && Convert.ToInt64(await Value(rollback.Id, "Version")) == rollback.Version
            && Convert.ToInt32(await Audits(rollback.Id)) == 0, "failed audit rolls back status version and audit");
        try { await execute($"UPDATE communications.Conversations SET Status='open' WHERE Id={rollback.Id}"); throw new InvalidOperationException("Missing SQL constraint"); }
        catch (SqlException) { check(true, "SQL enforces canonical status values"); }
        foreach (var initial in states)
        {
            var row = await Fixture(initial);
            var identity = "inbound-" + Guid.NewGuid().ToString("N");
            var occurredAt = at.AddHours(1);
            async Task Append(string key, string text)
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>();
                await new LocalCommunicationsStore(db, new EphemeralDataProtectionProvider(), TimeProvider.System)
                    .AppendAsync("ws_com1_a", row.Id, row.Participant, key, text, occurredAt);
            }
            await Append(identity, "Synthetic trusted inbound");
            var expected = initial is "PENDING" or "CLOSED" ? "OPEN" : initial;
            check((string?)await Value(row.Id, "Status") == expected && Convert.ToInt64(await Value(row.Id, "Version")) == row.Version + 1
                && (string?)await Value(row.Id, "AssigneeMemberId") == "member_a", $"trusted new inbound {initial} -> {expected}, one version and stable assignee");
            check(Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.Messages WHERE ConversationId={row.Id}")) == 2
                && Convert.ToInt64(await scalar($"SELECT MAX(Sequence) FROM communications.Messages WHERE ConversationId={row.Id}")) == row.Version + 1,
                "inbound preserves history and existing monotonic aggregate append sequence");
            var expectedAudits = initial is "CLOSED" or "PENDING" ? 2 : 1;
            check(Convert.ToInt32(await Audits(row.Id)) == expectedAudits, "inbound append and optional auto-reopen audit committed together");
            check((await Get($"/communications/conversations/{row.Id}")).Body.GetProperty("status").GetString() == expected
                && (await Get($"/communications/conversations/{row.Id}/messages")).Body.ToString().Contains("Synthetic trusted inbound"),
                "internal inbound state and history are visible through authorized production reads");
            // Close again so an exact duplicate cannot accidentally reopen it.
            await using (var scope = services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>();
                var conversation = await db.Conversations.SingleAsync(x => x.Id == row.Id);
                conversation.Transition("CLOSED"); await db.SaveChangesAsync();
            }
            var before = Convert.ToInt64(await Value(row.Id, "Version"));
            await Append(identity, "Synthetic trusted inbound");
            check((string?)await Value(row.Id, "Status") == "CLOSED" && Convert.ToInt64(await Value(row.Id, "Version")) == before
                && Convert.ToInt32(await Audits(row.Id)) == expectedAudits, "duplicate inbound does not reopen or add version/audit");
            foreach (var invalid in new[] { (identity, "conflicting replay"), (identity + "-invalid", "") })
            {
                try { await Append(invalid.Item1, invalid.Item2); throw new Exception("Invalid append accepted"); }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException) { }
            }
            check(Convert.ToInt64(await Value(row.Id, "Version")) == before && (string?)await Value(row.Id, "Status") == "CLOSED"
                && Convert.ToInt32(await Audits(row.Id)) == expectedAudits, "invalid inbound changes no state version or audit");
            await execute("CREATE TRIGGER communications.RejectInboundAudit ON communications.AuditRecords AFTER INSERT AS BEGIN THROW 51003, 'Fixture inbound audit rejected', 1; END");
            try { await Append(identity + "-rollback", "Synthetic failed inbound"); throw new Exception("Rollback expected"); }
            catch (DbUpdateException) { }
            await execute("DROP TRIGGER communications.RejectInboundAudit");
            check((string?)await Value(row.Id, "Status") == "CLOSED" && Convert.ToInt64(await Value(row.Id, "Version")) == before
                && Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.Messages WHERE ConversationId={row.Id}")) == 2
                && Convert.ToInt32(await Audits(row.Id)) == expectedAudits, "failed inbound append rolls back message auto-reopen version and audit");
        }
        foreach (var assignee in new string?[] { null, "member_inactive" })
        {
            var row = await Fixture("CLOSED", assignee);
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>();
            await new LocalCommunicationsStore(db, new EphemeralDataProtectionProvider(), TimeProvider.System)
                .AppendAsync("ws_com1_a", row.Id, row.Participant, "special-inbound-" + row.Id, "Synthetic internal inbound", at.AddHours(1));
            check((string?)await Value(row.Id, "Status") == "OPEN" && Convert.ToInt64(await Value(row.Id, "Version")) == row.Version + 1
                && (assignee is null ? await Value(row.Id, "AssigneeMemberId") is DBNull : (string?)await Value(row.Id, "AssigneeMemberId") == assignee),
                "system inbound retains null/inactive assignee without granting human access");
        }
        var closedQueue = await Fixture("CLOSED", null);
        var closedRows = new List<long>();
        for (var i = 0; i < 3; i++) closedRows.Add((await Fixture("CLOSED")).Id);
        await execute($"UPDATE communications.Conversations SET LastActivityAt=DATEADD(day,10,SYSDATETIMEOFFSET()) WHERE Id IN ({string.Join(',', closedRows)})");
        var active = await Get("/communications/conversations?limit=25&statusView=active");
        check(active.Body.GetProperty("items").GetArrayLength() == 25
            && active.Body.GetProperty("items").EnumerateArray().All(x => x.GetProperty("status").GetString() != "CLOSED"),
            "active SQL filter precedes Take even with newer closed records");
        var closedPage = await Get("/communications/conversations?limit=2&statusView=closed");
        check(closedPage.Body.GetProperty("items").GetArrayLength() == 2 && closedPage.Body.GetProperty("items").EnumerateArray().All(x => x.GetProperty("status").GetString() == "CLOSED"), "closed Inbox returns closed scoped page");
        var cursor = closedPage.Body.GetProperty("nextCursor").GetString();
        var nextClosed = await Get("/communications/conversations?limit=2&statusView=closed&cursor=" + Uri.EscapeDataString(cursor!));
        check(nextClosed.Status == HttpStatusCode.OK && !closedPage.Body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString())
            .Intersect(nextClosed.Body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString())).Any(), "closed cursor ordering has no repeated records");
        check((await Get("/communications/conversations?limit=2&statusView=active&cursor=" + Uri.EscapeDataString(cursor!))).Status == HttpStatusCode.BadRequest, "cursor cannot cross lifecycle views");
        var queue = await Get("/communications/conversations?view=unassigned");
        check(queue.Body.GetProperty("items").EnumerateArray().All(x => x.GetProperty("id").GetString() != closedQueue.Id.ToString()
            && x.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "id", "lastActivityAt" })), "active Queue excludes closed work without exposing status");
        // Keep COM-1/2A paging/browser fixtures unchanged after independent lifecycle scenarios.
        var list = string.Join(',', ids);
        await execute($"DELETE FROM communications.AuditRecords WHERE ConversationId IN ({list}); DELETE FROM communications.Messages WHERE ConversationId IN ({list}); DELETE FROM communications.Conversations WHERE Id IN ({list}); DELETE FROM access.RoleCapabilities WHERE Capability='communications.transition'");
    }
}
