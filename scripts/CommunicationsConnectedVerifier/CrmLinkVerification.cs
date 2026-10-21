using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Persistence;

internal static class CrmLinkVerification
{
    internal static Task Control(string action, Func<string, Task> execute) => execute(action switch
    {
        "crm-grant" => "IF NOT EXISTS(SELECT 1 FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability='communications.link') INSERT INTO access.RoleCapabilities VALUES ('role_a','communications.link')",
        "crm-revoke" => "DELETE FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability='contacts.read'",
        "crm-restore" => "IF NOT EXISTS(SELECT 1 FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability='contacts.read') INSERT INTO access.RoleCapabilities VALUES ('role_a','contacts.read')",
        "crm-hide" => "INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('browser_crm_fls','ws_com1_a','role_a','contacts','fullName','Hidden')",
        "crm-show" => "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='browser_crm_fls'",
        "crm-conflict" => "UPDATE communications.Conversations SET Version=Version+1 WHERE WorkspaceId='ws_com1_a' AND AssigneeMemberId='member_a'",
        _ => throw new InvalidOperationException("Unknown synthetic CRM control")
    });

    internal static async Task Run(IServiceProvider services, HttpClient http, IReadOnlyDictionary<string, string> tokens,
        Func<string, Task> execute, Func<string, Task<object?>> scalar, Action<bool, string> check)
    {
        async Task SeedLead(string id, string workspace = "ws_com1_a", string member = "member_a") => await execute($$"""
            INSERT INTO leads.Leads(LeadId,WorkspaceId,Profile,ScopeOwnerId,WorkState,Score,CreatedAt,UpdatedAt,Version,SearchText,PhoneSearchText)
            VALUES ('{{id}}','{{workspace}}','{"displayName":"Synthetic Lead {{id}}","phone":"0900000001","source":"synthetic","ownerId":"{{member}}","interestedProducts":[],"tags":[],"customFields":[]}',
            '{{member}}',2,0,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET(),0,'SYNTHETIC LEAD {{id.ToUpperInvariant()}}','');
            """);
        async Task SeedContact(string id, string workspace = "ws_com1_a", string member = "member_a") => await execute($$"""
            INSERT INTO contacts.Contacts(ContactId,WorkspaceId,OwnerId,FullName,Status,Version,CreatedAt,UpdatedAt,Profile)
            VALUES ('{{id}}','{{workspace}}','{{member}}','Synthetic Contact {{id}}','active',0,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET(),'{}');
            """);
        foreach (var id in new[] { "lead_crm_one", "lead_crm_two", "lead_crm_archived" }) await SeedLead(id);
        await SeedLead("lead_crm_foreign", "ws_com1_b"); await SeedLead("lead_crm_other", member: "member_b");
        foreach (var id in new[] { "contact_crm_one", "contact_crm_two", "contact_crm_archived" }) await SeedContact(id);
        await SeedContact("contact_crm_foreign", "ws_com1_b"); await SeedContact("contact_crm_other", member: "member_b");
        await execute("UPDATE leads.Leads SET ArchivedAt=SYSDATETIMEOFFSET() WHERE LeadId='lead_crm_archived'; UPDATE contacts.Contacts SET ArchivedAt=SYSDATETIMEOFFSET(),Status='archived' WHERE ContactId='contact_crm_archived'");
        await execute("INSERT INTO access.RoleCapabilities VALUES ('role_a','leads.read'),('role_a','contacts.read'),('role_b','communications.link'); INSERT INTO access.RoleDataScopes (PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('crm_lead_scope','ws_com1_a','role_a','leads','Own','[]'),('crm_contact_scope','ws_com1_a','role_a','contacts','Own','[]')");
        long conversation;
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>();
            var account = await db.Accounts.SingleAsync(x => x.WorkspaceId == "ws_com1_a");
            var row = new Conversation(account, "crm-link-test", DateTimeOffset.UtcNow.AddDays(-50), "member_a");
            db.Add(row); await db.SaveChangesAsync(); conversation = row.Id;
            var participant = await db.Participants.SingleAsync(x => x.AccountId == account.Id);
            db.Messages.Add(row.Append(participant, "crm-link-test-message", "SYNTHETIC_LINK_BODY", row.LastActivityAt)); await db.SaveChangesAsync();
        }
        var path = $"/communications/conversations/{conversation}";
        async Task<(HttpStatusCode Status, JsonElement Body)> Request(string suffix, object? body = null, string? actor = "a", string workspace = "ws_com1_a", string? raw = null, string? absolute = null, string? idem = null)
        {
            using var request = new HttpRequestMessage(body is null && raw is null ? HttpMethod.Get : HttpMethod.Post, absolute ?? path + suffix);
            if (actor is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[actor]);
            request.Headers.Add("X-Workspace-Id", workspace); request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
            request.Headers.Add("X-Correlation-Id", "crm-link-correlation");
            if (absolute is not null) { request.Headers.Add("If-Match", "\"0\""); request.Headers.Add("Idempotency-Key", idem ?? Guid.NewGuid().ToString("N")); }
            if (body is not null || raw is not null) request.Content = new StringContent(raw ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            check(!text.Contains("SELECT ") && !text.Contains("Exception"), "CRM HTTP sanitizes internal diagnostics");
            if (response.IsSuccessStatusCode && absolute is null) check(response.Headers.CacheControl?.NoStore == true, "CRM response forbids caching");
            return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
        }
        Task<(HttpStatusCode Status, JsonElement Body)> Link(string? type, string? target, long version, string? actor = "a", string workspace = "ws_com1_a") =>
            Request(type is null ? "/crm-link/unlink" : "/crm-link", type is null ? new { expectedVersion = version } : (object)new { targetType = type, targetId = target, expectedVersion = version }, actor, workspace);
        async Task<long> Version() => Convert.ToInt64(await scalar($"SELECT Version FROM communications.Conversations WHERE Id={conversation}"));
        async Task<int> AuditCount() => Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.AuditRecords WHERE ConversationId={conversation}"));
        var initialActivity = await scalar($"SELECT LastActivityAt FROM communications.Conversations WHERE Id={conversation}");
        check((await Link("LEAD", "lead_crm_one", 1)).Status == HttpStatusCode.Forbidden, "no implicit Communications link capability");
        await Control("crm-grant", execute);
        await execute("DELETE FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability='communications.read'");
        check((await Link("LEAD", "lead_crm_one", 1)).Status == HttpStatusCode.Forbidden, "link capability without Communications read is insufficient");
        await execute("INSERT INTO access.RoleCapabilities VALUES ('role_a','communications.read')");
        foreach (var restriction in new[] { "ReadOnly", "Hidden" })
        {
            await execute($"INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('crm_link_write_fls','ws_com1_a','role_a','communications','crmLink','{restriction}')");
            check((await Link("LEAD", "lead_crm_one", 1)).Status == HttpStatusCode.Forbidden, "Communications link field write restrictions enforced");
            await execute("DELETE FROM access.RoleFieldSecurity WHERE PolicyId='crm_link_write_fls'");
        }
        foreach (var actor in new string?[] { null, "b", "admin", "manager", "triage" })
            check((await Link("LEAD", "lead_crm_one", 1, actor)).Status == (actor is null ? HttpStatusCode.Unauthorized : actor == "b" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden), "anonymous other-assignee and default roles denied CRM mutation");
        check((await Link("LEAD", "lead_crm_one", 1, workspace: "ws_com1_b")).Status == HttpStatusCode.Forbidden, "foreign trusted Workspace lacks link grant");
        check((await Link("LEAD", "lead_crm_one", 1, "b", "ws_com1_b")).Status == HttpStatusCode.Forbidden, "untrusted Workspace identifier denied");
        await execute("UPDATE workspace.Memberships SET Status='Suspended' WHERE MembershipId='membership_a'");
        check((await Link("LEAD", "lead_crm_one", 1)).Status == HttpStatusCode.Forbidden, "inactive membership rejects old token link");
        await execute("UPDATE workspace.Memberships SET Status='Active' WHERE MembershipId='membership_a'");
        foreach (var item in new[] { ("LEAD", "missing"), ("LEAD", "lead_crm_foreign"), ("LEAD", "lead_crm_other"), ("LEAD", "lead_crm_archived"), ("CONTACT", "contact_crm_foreign"), ("CONTACT", "contact_crm_other"), ("CONTACT", "contact_crm_archived") })
        {
            var result = await Link(item.Item1, item.Item2, 1);
            check(result.Status == HttpStatusCode.NotFound && !result.Body.ToString().Contains(item.Item2), "missing foreign out-of-scope archived targets share safe denial");
        }
        foreach (var raw in new[] { "{}", "[]", "{", "{\"expectedVersion\":1,\"workspaceId\":\"forged\"}", "{\"targetType\":\"CUSTOMER\",\"targetId\":\"x\",\"expectedVersion\":1}", "{\"expectedVersion\":1,\"expectedVersion\":1}" })
            check((await Request("/crm-link", raw: raw)).Status == HttpStatusCode.BadRequest, "CRM mutation strict DTO rejects forged malformed duplicate fields");
        foreach (var item in new (string? Type, string? Target)[] { ("LEAD", "lead_crm_one"), ("CONTACT", "contact_crm_one"), ("LEAD", "lead_crm_one"), ("LEAD", "lead_crm_two"), (null, null), ("CONTACT", "contact_crm_two"), (null, null) })
        {
            var version = await Version(); var count = await AuditCount();
            var result = await Link(item.Type, item.Target, version);
            check(result.Status == HttpStatusCode.OK && result.Body.GetProperty("changed").GetBoolean(), $"manual {item.Type ?? "UNLINK"} commits");
            check(await Version() == version + 1 && await AuditCount() == count + 1, "link exact aggregate version and body-free audit delta");
            check((await Link(item.Type, item.Target, version)).Status == HttpStatusCode.Conflict, "stale retry always conflicts");
            var replay = await Link(item.Type, item.Target, version + 1);
            check(replay.Status == HttpStatusCode.OK && !replay.Body.GetProperty("changed").GetBoolean() && await AuditCount() == count + 1 && await Version() == version + 1, "matching current link or empty unlink no-op has no new audit/version");
        }
        check(Equals(initialActivity, await scalar($"SELECT LastActivityAt FROM communications.Conversations WHERE Id={conversation}"))
            && (string?)await scalar($"SELECT Status FROM communications.Conversations WHERE Id={conversation}") == "NEW"
            && (string?)await scalar($"SELECT AssigneeMemberId FROM communications.Conversations WHERE Id={conversation}") == "member_a"
            && Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.Messages WHERE ConversationId={conversation} AND Sequence=1 AND Text='SYNTHETIC_LINK_BODY'")) == 1, "all link changes preserve activity state assignee and message identity/sequence/text");
        await Link("LEAD", "LEAD_CRM_ONE", await Version());
        var canonicalVersion = await Version(); var canonicalAudit = await AuditCount();
        var canonical = await Link("LEAD", "lead_crm_one", canonicalVersion);
        check(canonical.Status == HttpStatusCode.OK && !canonical.Body.GetProperty("changed").GetBoolean() && await Version() == canonicalVersion && await AuditCount() == canonicalAudit,
            "owner-returned canonical identity makes case-alias retry a no-op");
        var secondConversation = Convert.ToInt64(await scalar("SELECT Id FROM communications.Conversations WHERE WorkspaceId='ws_com1_a' AND LocalIdentity='fixture-1'"));
        var second = await Request("", new { targetType = "LEAD", targetId = "lead_crm_one", expectedVersion = 0 },
            absolute: $"/communications/conversations/{secondConversation}/crm-link");
        check(second.Status == HttpStatusCode.OK && Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.ConversationRecordLinks WHERE ConversationId IN ({conversation},{secondConversation}) AND TargetId='lead_crm_one' AND UnlinkedAt IS NULL")) == 2,
            "two Conversations can independently link the same authorized CRM record");
        await execute($"DELETE FROM communications.ConversationRecordLinks WHERE ConversationId={secondConversation}; DELETE FROM communications.AuditRecords WHERE ConversationId={secondConversation}; UPDATE communications.Conversations SET Version=0 WHERE Id={secondConversation}");
        await Link(null, null, await Version());
        var expected = await Version();
        var race = await Task.WhenAll(Link("LEAD", "lead_crm_one", expected), Link("CONTACT", "contact_crm_one", expected));
        check(race.Count(x => x.Status == HttpStatusCode.OK) == 1 && race.Count(x => x.Status == HttpStatusCode.Conflict) == 1, "concurrent distinct links have one winner and one409");
        check(Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.ConversationRecordLinks WHERE ConversationId={conversation} AND UnlinkedAt IS NULL")) == 1, "SQL retains exactly one active primary link");
        try
        {
            await execute($"INSERT INTO communications.ConversationRecordLinks SELECT NEWID(),WorkspaceId,AccountId,ConversationId,TargetType,TargetId,LinkedAt,LinkedBy,NULL,NULL FROM communications.ConversationRecordLinks WHERE ConversationId={conversation} AND UnlinkedAt IS NULL");
            check(false, "filtered index must reject duplicate");
        }
        catch (SqlException error) when (error.Number is 2601 or 2627) { check(true, "SQL filtered unique index independently rejects second active link"); }
        try
        {
            await execute($"INSERT INTO communications.ConversationRecordLinks (Id,WorkspaceId,AccountId,ConversationId,TargetType,TargetId,LinkedAt,LinkedBy,UnlinkedAt,UnlinkedBy) VALUES (NEWID(),'ws_com1_a','00000000-0000-0000-0000-000000000000',{conversation},'LEAD','lead_crm_one',SYSDATETIMEOFFSET(),'member_a',SYSDATETIMEOFFSET(),'member_a')");
            check(false, "link must bind correct workspace account conversation");
        }
        catch (SqlException error) when (error.Number == 547) { check(true, "owner-local FK rejects incorrect account/conversation binding"); }
        expected = await Version(); var auditBefore = await AuditCount();
        await execute("CREATE TRIGGER communications.RejectCrmAudit ON communications.AuditRecords AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Operation='CONVERSATION_CRM_LINK_CHANGED') THROW 51000,'Synthetic audit rejection',1; END");
        check((await Link(null, null, expected)).Status == HttpStatusCode.InternalServerError, "audit failure not acknowledged as committed");
        await execute("DROP TRIGGER communications.RejectCrmAudit");
        check(await Version() == expected && await AuditCount() == auditBefore && Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.ConversationRecordLinks WHERE ConversationId={conversation} AND UnlinkedAt IS NULL")) == 1, "SQL audit failure rolls back old-link ending aggregate version and audit");
        await Link("CONTACT", "contact_crm_one", await Version());
        check((await Request("/crm-link")).Body.GetProperty("target").GetProperty("targetId").GetString() == "contact_crm_one", "owner-authorized context dereferenced");
        foreach (var field in new[] { "crmLink", "crmContext" })
        {
            await execute($"INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('crm_projection_fls','ws_com1_a','role_a','communications','{field}','Hidden')");
            var withheld = await Request("/crm-link");
            check(withheld.Body.GetProperty("state").GetString() == "UNAVAILABLE" && !withheld.Body.ToString().Contains("contact_crm_one"), "Communications link/context FLS independently withholds CRM identity");
            await execute("DELETE FROM access.RoleFieldSecurity WHERE PolicyId='crm_projection_fls'");
        }
        await execute("UPDATE contacts.Contacts SET ArchivedAt=SYSDATETIMEOFFSET(),Status='archived' WHERE ContactId='contact_crm_one'");
        var historical = (await Request("/crm-link")).Body;
        check(historical.GetProperty("target").GetProperty("targetId").GetString() == "contact_crm_one" && !historical.GetProperty("target").GetProperty("eligible").GetBoolean(), "later archival preserves existing manual link with owner eligibility withheld from new actions");
        await execute("UPDATE contacts.Contacts SET ArchivedAt=NULL,Status='active' WHERE ContactId='contact_crm_one'");
        await Control("crm-hide", execute);
        var redacted = await Request("/crm-link");
        check(!redacted.Body.GetProperty("target").TryGetProperty("label", out _), "Contact fullName FLS withholds linked label");
        check((await Request("/crm-candidates?targetType=CONTACT&search=Synthetic")).Body.GetArrayLength() == 0, "hidden name cannot be searched as existence oracle");
        await Control("crm-show", execute); await Control("crm-revoke", execute);
        var denied = await Request("/crm-link");
        check(denied.Status == HttpStatusCode.OK && denied.Body.GetProperty("state").GetString() == "UNAVAILABLE" && !denied.Body.ToString().Contains("contact_crm_one"), "CRM revocation withholds identity label and navigation");
        check((await Request("/messages")).Body.ToString().Contains("SYNTHETIC_LINK_BODY"), "CRM denial preserves independently authorized messages");
        var unlink = await Link(null, null, await Version());
        check(unlink.Status == HttpStatusCode.OK && !unlink.Body.ToString().Contains("contact_crm_one"), "unlink after CRM revocation succeeds without echoing prior target");
        await Control("crm-restore", execute);
        foreach (var resource in new[] { "contacts", "leads" })
        {
            await execute($"INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('crm_id_fls','ws_com1_a','role_a','{resource}','id','Hidden')");
            check((await Link(resource == "contacts" ? "CONTACT" : "LEAD", resource == "contacts" ? "contact_crm_one" : "lead_crm_one", await Version())).Status == HttpStatusCode.NotFound, "owner identifier FLS refuses new target");
            await execute("DELETE FROM access.RoleFieldSecurity WHERE PolicyId='crm_id_fls'");
        }
        check(Convert.ToInt32(await scalar("SELECT COUNT(*) FROM leads.Leads WHERE Version<>0")) == 0
            && Convert.ToInt32(await scalar("SELECT COUNT(*) FROM contacts.Contacts WHERE Version<>0")) == 0, "link commands never mutate CRM owner aggregate versions");
        // Real workflow HTTP, real owner participants, durable owner commits; no automatic relink.
        await execute("INSERT INTO access.RoleCapabilities VALUES ('role_a','leads.qualify'),('role_a','tasks.create'),('role_a','deals.create'); INSERT INTO access.RoleDataScopes (PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('crm_task_scope','ws_com1_a','role_a','tasks','Workspace','[]'),('crm_deal_scope','ws_com1_a','role_a','deals','Workspace','[]')");
        foreach (var kind in new[] { "nurture", "opportunity" })
        foreach (var mode in new[] { "NEW", "EXISTING" })
        {
            var lead = $"lead_crm_{kind}_{mode.ToLowerInvariant()}"; await SeedLead(lead);
            await Link("LEAD", lead, await Version()); var linkVersion = await Version();
            var relationship = new { kind = "CONTACT", mode, selectedId = mode == "EXISTING" ? "contact_crm_one" : null,
                contact = new { displayName = $"Synthetic {kind} {mode}", email = $"{kind}.{mode.ToLowerInvariant()}@example.invalid", phone = "0900000001" } };
            object body = kind == "nurture" ? new { relationship, revisitAt = "2026-10-25T09:00:00Z", reason = "Synthetic follow-up" }
                : new { relationship, deal = new { name = "Synthetic opportunity", ownerId = "member_a", needSummary = "Synthetic business need", expectedCloseDate = "2026-11-01", estimatedValue = new { amount = "1000", currency = "VND" }, interestedProductIds = Array.Empty<string>() } };
            var idem = $"crm-{kind}-{mode}";
            var result = await Request("", body, absolute: $"/workflows/lead-qualification/{lead}/{kind}", idem: idem);
            check(result.Status == HttpStatusCode.OK, $"real {kind} qualification {mode} succeeds (status {result.Status}; code {(result.Body.ValueKind == JsonValueKind.Object && result.Body.TryGetProperty("code", out var errorCode) ? errorCode.GetString() : "none")})");
            var contact = result.Body.GetProperty("result").GetProperty("contactId").GetString();
            var context = (await Request("/crm-link")).Body;
            check(context.GetProperty("target").GetProperty("targetId").GetString() == lead && await Version() == linkVersion, "qualification preserves Lead link and Conversation version");
            check(context.GetProperty("successor").GetProperty("targetId").GetString() == contact, "successor requires owner-confirmed terminal CONTACT relationship");
            await execute("INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('crm_relationship_fls','ws_com1_a','role_a','leads','relationshipRef','Hidden')");
            check(!(await Request("/crm-link")).Body.TryGetProperty("successor", out _), "hidden Lead relationship suppresses successor identity");
            await execute("DELETE FROM access.RoleFieldSecurity WHERE PolicyId='crm_relationship_fls'");
            await Control("crm-revoke", execute);
            check(!(await Request("/crm-link")).Body.TryGetProperty("successor", out _), "Contact read revocation suppresses successor");
            await Control("crm-restore", execute);
            check((await Link("CONTACT", contact, await Version())).Status == HttpStatusCode.OK, "successor replacement requires explicit authorized manual command");
            check(Convert.ToInt32(await scalar($"SELECT COUNT(*) FROM communications.ConversationRecordLinks WHERE ConversationId={conversation} AND TargetId='{lead}' AND UnlinkedAt IS NOT NULL")) == 1, "manual successor replacement retains historical Lead link");
        }
        var partialLead = "lead_crm_partial"; await SeedLead(partialLead);
        await Link("LEAD", partialLead, await Version()); var partialVersion = await Version();
        await execute("CREATE TRIGGER leads.RejectSyntheticClosure ON leads.Leads AFTER UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE LeadId='lead_crm_partial' AND RelationshipId IS NOT NULL) THROW 51000,'Synthetic closure failure',1; END");
        var partialBody = new { relationship = new { kind = "CONTACT", mode = "NEW", contact = new { displayName = "Synthetic partial", email = "partial@example.invalid", phone = "0900000001" } }, revisitAt = "2026-10-25T09:00:00Z", reason = "Synthetic partial retry" };
        var partial = await Request("", partialBody, absolute: $"/workflows/lead-qualification/{partialLead}/nurture", idem: "crm-partial-retry");
        check(partial.Status != HttpStatusCode.OK && Convert.ToInt32(await scalar("SELECT COUNT(*) FROM contacts.Contacts WHERE NormalizedWorkEmail='PARTIAL@EXAMPLE.INVALID'")) == 1, "partial workflow really commits Contact before failed Lead closure");
        check(!(await Request("/crm-link")).Body.TryGetProperty("successor", out _) && await Version() == partialVersion, "partial qualification never infers successor from committed Contact or anchor");
        await execute("DROP TRIGGER leads.RejectSyntheticClosure");
        check((await Request("", partialBody, absolute: $"/workflows/lead-qualification/{partialLead}/nurture", idem: "crm-partial-retry")).Status == HttpStatusCode.OK, "same workflow retry converges after Lead closure failure");
        check((await Request("/crm-link")).Body.GetProperty("target").GetProperty("targetId").GetString() == partialLead && await Version() == partialVersion, "successful qualification retry retains manual Lead link");
        await execute("INSERT INTO access.RoleCapabilities VALUES ('role_a','leads.convert_to_customer'),('role_a','organizations.read'); INSERT INTO access.RoleDataScopes (PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES ('crm_org_scope','ws_com1_a','role_a','organizations','Own','[]')");
        await execute("INSERT INTO organizations.Organizations (WorkspaceId,OrganizationId,DisplayName,Status,OwnerId,Version,CreatedAt,UpdatedAt,Profile,SearchText) VALUES ('ws_com1_a','organization_crm','Synthetic organization','active','member_a',0,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET(),'{}','SYNTHETIC ORGANIZATION')");
        foreach (var subjectType in new[] { "CONTACT", "ORGANIZATION_ACCOUNT" })
        {
            var customerLead = "lead_crm_customer_" + subjectType.ToLowerInvariant(); await SeedLead(customerLead);
            await Link("LEAD", customerLead, await Version()); var originalVersion = await Version();
            var conversion = await Request("", new { accountSubject = new { type = subjectType, mode = "EXISTING", id = subjectType == "CONTACT" ? "contact_crm_one" : "organization_crm" } },
                absolute: $"/workflows/lead-customer-conversion/{customerLead}", idem: "crm-customer-" + subjectType);
            check(conversion.Status == HttpStatusCode.OK, $"real Lead to Customer conversion {subjectType} succeeds");
            var context = (await Request("/crm-link")).Body;
            check(context.GetProperty("target").GetProperty("targetId").GetString() == customerLead && !context.TryGetProperty("successor", out _) && await Version() == originalVersion,
                "CustomerRef never implies Contact successor or automatic link migration");
        }
        await execute($"DELETE FROM communications.ConversationRecordLinks WHERE ConversationId={conversation}; DELETE FROM communications.AuditRecords WHERE ConversationId={conversation}; DELETE FROM communications.Messages WHERE ConversationId={conversation}; DELETE FROM communications.Conversations WHERE Id={conversation}; DELETE FROM access.RoleCapabilities WHERE Capability='communications.link'");
        check(Convert.ToInt32(await scalar("SELECT COUNT(*) FROM communications.ConversationRecordLinks")) == 0, "CRM test rows retired before existing browser pagination regressions");
    }
}
