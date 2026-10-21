using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using UnicoreCRM.Communications;
using UnicoreCRM.Communications.Contracts;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Persistence;
using UnicoreCRM.Platform;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Crm;
using UnicoreCRM.Operations;
using UnicoreCRM.Workflows;
using UnicoreCRM.Workflows.Atomic.Contracts;
using UnicoreCRM.Sales;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks++; Console.WriteLine($"PASS {name}"); }
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
var artifacts = Environment.GetEnvironmentVariable("COMMUNICATIONS_VERIFICATION_ARTIFACTS")
    ?? Path.Combine(root, "review-artifacts/communications-com2c-manual-crm-linking-20261016");
Directory.CreateDirectory(artifacts);
var database = $"UnicoreCRM_COM2C_{Guid.NewGuid():N}";
var files = Path.Combine(artifacts, "sql-disposable", database);
Directory.CreateDirectory(files);
var masterString = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=True;TrustServerCertificate=True;Pooling=False";
var connection = new SqlConnectionStringBuilder(masterString) { InitialCatalog = database }.ConnectionString;
await using var master = new SqlConnection(masterString);
await master.OpenAsync();
async Task Execute(string sql, string? target = null)
{
    await using var dbConnection = new SqlConnection(target ?? connection);
    await dbConnection.OpenAsync();
    await using var command = new SqlCommand(sql, dbConnection);
    await command.ExecuteNonQueryAsync();
}
async Task<object?> Scalar(string sql, string? target = null)
{
    await using var dbConnection = new SqlConnection(target ?? connection);
    await dbConnection.OpenAsync();
    await using var command = new SqlCommand(sql, dbConnection);
    return await command.ExecuteScalarAsync();
}
Check(await Scalar($"SELECT DB_ID(N'{database}')", masterString) is DBNull, "new task database absent before creation");
var created = false;
try
{
    var mdf = Path.Combine(files, "communications.mdf").Replace("'", "''");
    var ldf = Path.Combine(files, "communications_log.ldf").Replace("'", "''");
    await Execute($"CREATE DATABASE [{database}] ON PRIMARY (NAME=N'{database}', FILENAME=N'{mdf}') LOG ON (NAME=N'{database}_log', FILENAME=N'{ldf}')", masterString);
    created = true;
    var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
    var controlKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
    var builder = WebApplication.CreateBuilder();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
        ["ConnectionStrings:UnicoreCRM"] = connection,
        ["IdentityAuth:Jwt:SigningKey"] = key, ["IdentityAuth:RefreshTokenPepper"] = key,
        ["IdentityAuth:Jwt:Issuer"] = "Com1Fixture", ["IdentityAuth:Jwt:Audience"] = "Com1Fixture"
    });
    builder.Services.AddPlatformModule(builder.Configuration);
    builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(
        new UnicoreCRM.ApiHost.Serialization.UtcDateTimeOffsetJsonConverter()));
    builder.Services.AddCommunicationsModule(builder.Configuration);
    builder.Services.AddCrmModule(builder.Configuration);
    builder.Services.AddSalesModule(builder.Configuration);
    builder.Services.AddOperationsModule(builder.Configuration);
    builder.Services.AddWorkflowsModule(builder.Configuration);
    builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
    await using var connectionFixture = args.Contains("--telegram-connections", StringComparer.Ordinal)
        ? await TelegramConnectionVerification.StartAsync(artifacts, database, builder.Services) : null;
    // Unrelated email dispatcher never starts in this synthetic read-only host.
    foreach (var service in builder.Services.Where(s => s.ServiceType == typeof(IHostedService)
        && (s.ImplementationType?.Name == "IdentityEmailOutboxDispatcher" || s.ImplementationType?.Name == "RecoveryService")).ToArray()) builder.Services.Remove(service);
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
        .AllowCredentials().AllowAnyHeader().AllowAnyMethod()));
    await using var app = builder.Build();
    await using (var scope = app.Services.CreateAsyncScope())
    {
        foreach (var name in new[] { "UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence.IdentityAuthDbContext",
            "UnicoreCRM.Platform.Workspace.Infrastructure.Persistence.WorkspaceDbContext",
            "UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.AccessControlDbContext" })
        {
            var type = typeof(PlatformModule).Assembly.GetType(name) ?? throw new InvalidOperationException("Missing fixture migration context");
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(type);
            await context.Database.MigrateAsync();
        }
        foreach (var name in new[] {
            "UnicoreCRM.Crm.Leads.Infrastructure.Persistence.LeadsDbContext",
            "UnicoreCRM.Crm.Organizations.Infrastructure.Persistence.OrganizationsDbContext",
            "UnicoreCRM.Crm.Customers.Infrastructure.Persistence.CustomersDbContext",
            "UnicoreCRM.Crm.Contacts.Infrastructure.Persistence.ContactsDbContext",
            "UnicoreCRM.Crm.Deals.Infrastructure.Persistence.DealsDbContext",
            "UnicoreCRM.Operations.Tasks.Infrastructure.Persistence.TasksDbContext",
            "UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence.WorkflowsDbContext" })
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(x => x is not null)
                ?? throw new InvalidOperationException("Missing CRM fixture context");
            await ((DbContext)scope.ServiceProvider.GetRequiredService(type)).Database.MigrateAsync();
        }
        var communications = scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>();
        await communications.GetService<IMigrator>().MigrateAsync("20261015132127_ConversationAssigneeReadScope");
        await communications.Database.ExecuteSqlRawAsync("INSERT INTO communications.AuditRecords (Id,WorkspaceId,ConversationId,MessageId,RecordedAt) VALUES (NEWID(),'ws_upgrade',0,NEWID(),SYSDATETIMEOFFSET())");
        await communications.GetService<IMigrator>().MigrateAsync("20261015142422_ConversationClaimAudit");
        await communications.Database.ExecuteSqlRawAsync("""
            INSERT INTO communications.ChannelAccounts (Id,WorkspaceId,LocalIdentity) VALUES ('11111111-1111-1111-1111-111111111111','ws_backfill','synthetic-backfill');
            INSERT INTO communications.Conversations (WorkspaceId,AccountId,LocalIdentity,LastActivityAt,Version,AssigneeMemberId)
              VALUES ('ws_backfill','11111111-1111-1111-1111-111111111111','synthetic-backfill',SYSDATETIMEOFFSET(),17,'member_backfill');
            """);
        try { await communications.Database.MigrateAsync(); throw new InvalidOperationException("Backfill guard missing"); }
        catch (SqlException error) when (error.Number == 51001) { Check(true, "populated schema refuses implicit historical status backfill"); }
        if (args.Contains("--foundation", StringComparer.Ordinal))
        {
            // Foundation never bypasses a populated lifecycle guard. Remove only the synthetic
            // guard-test conversation, retaining its legacy account to verify additive metadata.
            await communications.Database.ExecuteSqlRawAsync("DELETE FROM communications.Conversations WHERE WorkspaceId='ws_backfill'");
            await communications.Database.MigrateAsync();
            var legacyAccount = await communications.Accounts.AsNoTracking().SingleAsync(x => x.WorkspaceId == "ws_backfill");
            Check(legacyAccount.ProviderKey is null && legacyAccount.Status is null && legacyAccount.ConfigurationVersion == 0,
                "foundation migration preserves legacy account identity without adoption/backfill");
        }
        else
        {
        await communications.Database.OpenConnectionAsync();
        try
        {
            await communications.Database.ExecuteSqlRawAsync("EXEC sys.sp_set_session_context @key=N'communications.synthetic_lifecycle_backfill', @value=1");
            await communications.Database.MigrateAsync();
            await communications.Database.ExecuteSqlRawAsync("EXEC sys.sp_set_session_context @key=N'communications.synthetic_lifecycle_backfill', @value=NULL");
        }
        finally { await communications.Database.CloseConnectionAsync(); }
        var backfilled = await communications.Conversations.AsNoTracking().SingleAsync(x => x.WorkspaceId == "ws_backfill");
        Check(backfilled.Status == "NEW" && backfilled.Version == 17 && backfilled.AssigneeMemberId == "member_backfill", "explicit synthetic backfill preserves version and assignee");
        }
        await communications.Database.ExecuteSqlRawAsync("DELETE FROM communications.Conversations WHERE WorkspaceId='ws_backfill'; DELETE FROM communications.ChannelAccounts WHERE WorkspaceId='ws_backfill'");
        var upgradedAudit = await communications.Audit.AsNoTracking().SingleAsync(x => x.WorkspaceId == "ws_upgrade");
        Check(upgradedAudit.Operation == "MESSAGE_APPENDED" && upgradedAudit.ActorMemberId is null && upgradedAudit.MessageId is not null,
            "Claim migration preserves existing append audit and backfills operation");
        await communications.Database.ExecuteSqlRawAsync("DELETE FROM communications.AuditRecords WHERE WorkspaceId='ws_upgrade'");
        Check(!communications.Database.HasPendingModelChanges(), "current Communications snapshot matches model");
        Check((await communications.Database.GetAppliedMigrationsAsync()).Count() == 9, "all nine Communications migrations applied");
    }
    await Execute("""
        INSERT INTO workspace.Workspaces (WorkspaceId,[Key],[Name],LogoText,CreatedAt) VALUES
          ('ws_com1_a','com1-a','Synthetic A','A',SYSDATETIMEOFFSET()),('ws_com1_b','com1-b','Synthetic B','B',SYSDATETIMEOFFSET());
        """);
    // Real sign-in/bootstrap shell fixture: password hashing format is the framework's production
    // hasher format. Only synthetic local accounts are seeded; no identity provider is contacted.
    var fixturePassword = "Synthetic-Claim-Only-2026!";
    var passwordHash = new PasswordHasher<object>().HashPassword(new object(), fixturePassword).Replace("'", "''");
    await Execute("""
        INSERT INTO workspace.BootstrapProjections (WorkspaceId,ContextVersion,ConfigurationVersion,Locale,TimeZone,BaseCurrency,CapabilitiesJson,EnabledModuleKeysJson,AvailableProductSpacesJson) VALUES
          ('ws_com1_a',1,1,'en','Asia/Ho_Chi_Minh','VND','[]','[]','["crm"]'),
          ('ws_com1_b',1,1,'en','Asia/Ho_Chi_Minh','VND','[]','[]','["crm"]');
        """);
    var actors = new[] { "a", "b", "admin", "manager", "triage" };
    foreach (var actor in actors)
    {
        var template = actor == "admin" ? "workspace-administrator" : actor == "manager" ? "sales-manager" : "sales-representative";
        await Execute($$"""
            INSERT INTO iam.Accounts (AccountId,MemberId,Email,NormalizedEmail,DisplayName,Status,CreatedAt,EmailVerifiedAt)
              VALUES ('account_{{actor}}','member_{{actor}}','{{actor}}@example.invalid','{{actor.ToUpperInvariant()}}@EXAMPLE.INVALID','Synthetic {{actor}}','Active',SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET());
            INSERT INTO iam.Sessions (SessionId,AccountId,RefreshTokenHash,RefreshCounter,Status,IssuedAt,LastSeenAt,IdleExpiresAt,AbsoluteExpiresAt,DeviceId,DeviceLabel)
              VALUES ('session_{{actor}}','account_{{actor}}','fixture',0,'Active',SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET(),DATEADD(day,1,SYSDATETIMEOFFSET()),DATEADD(day,1,SYSDATETIMEOFFSET()),'fixture','fixture');
            INSERT INTO workspace.Memberships (MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt)
              VALUES ('membership_{{actor}}','ws_com1_a','account_{{actor}}','member_{{actor}}','Active',SYSDATETIMEOFFSET());
            INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,SourceTemplateId,IsActive,Version,CreatedAt,UpdatedAt)
              VALUES ('role_{{actor}}','ws_com1_a','Synthetic {{actor}}','SYNTHETIC {{actor.ToUpperInvariant()}}','{{template}}',1,0,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET());
            INSERT INTO access.MembershipRoleAssignments (AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt)
              VALUES ('assignment_{{actor}}','ws_com1_a','membership_{{actor}}','role_{{actor}}',SYSDATETIMEOFFSET());
            INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('role_{{actor}}','workspace.context.resolve');
            """);
    }
    await Execute("""
        INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES
          ('role_a','communications.read'),('role_a','communications.queue.read'),('role_b','communications.read'),
          ('role_admin','studio.configure'),('role_admin','studio.read'),('role_manager','leads.read'),('role_triage','communications.queue.read');
        INSERT INTO access.RoleDataScopes (PolicyId,WorkspaceId,RoleId,ResourceKey,Scope,AllowedOwnerIdsJson) VALUES
          ('scope_a','ws_com1_a','role_a','communications','Workspace','[]'),('scope_b','ws_com1_a','role_b','communications','Own','[]');
        INSERT INTO workspace.Memberships (MembershipId,WorkspaceId,AccountId,MemberId,Status,CreatedAt)
          VALUES ('membership_a_foreign','ws_com1_b','account_a','member_a','Active',SYSDATETIMEOFFSET());
        INSERT INTO access.Roles (RoleId,WorkspaceId,Name,NormalizedName,IsActive,Version,CreatedAt,UpdatedAt)
          VALUES ('role_foreign','ws_com1_b','Synthetic foreign','SYNTHETIC FOREIGN',1,0,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET());
        INSERT INTO access.MembershipRoleAssignments (AssignmentId,WorkspaceId,MembershipId,RoleId,AssignedAt)
          VALUES ('assignment_foreign','ws_com1_b','membership_a_foreign','role_foreign',SYSDATETIMEOFFSET());
        INSERT INTO access.RoleCapabilities (RoleId,Capability) VALUES ('role_foreign','communications.read'),('role_foreign','workspace.context.resolve');
        """);
    await Execute($"INSERT INTO iam.Credentials (AccountId,PasswordHash,UpdatedAt) VALUES ('account_a','{passwordHash}',SYSDATETIMEOFFSET())");
    long id, unassignedId, bId, foreignId;
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>();
        var account = new ChannelAccount("ws_com1_a", "fixture");
        var foreignAccount = new ChannelAccount("ws_com1_b", "fixture");
        var participant = new ExternalParticipant(account, "SYNTHETIC_PRIVATE_PARTICIPANT");
        var at = DateTimeOffset.UtcNow;
        var conversations = Enumerable.Range(0, 26).Select(i => new Conversation(account, $"fixture-{i}", at.AddMinutes(-i), "member_a")).ToArray();
        var unassigned = new Conversation(account, "SYNTHETIC_PRIVATE_SUBJECT", at.AddDays(1));
        var bConversation = new Conversation(account, "other-assignee", at.AddDays(2), "member_b");
        var foreign = new Conversation(foreignAccount, "foreign", at, "member_a");
        db.AddRange(account, foreignAccount, participant, unassigned, bConversation, foreign);
        db.AddRange(conversations);
        await db.SaveChangesAsync();
        id = conversations[0].Id; unassignedId = unassigned.Id; bId = bConversation.Id; foreignId = foreign.Id;
        for (var i = 0; i < 27; i++) db.Messages.Add(conversations[0].Append(participant, $"message-{i}", $"SYNTHETIC_BODY_{i}", at.AddSeconds(i)));
        db.Messages.Add(unassigned.Append(participant, "unassigned-private", "SYNTHETIC_UNASSIGNED_BODY", at));
        await db.SaveChangesAsync();
    }
    app.UseCors();
    app.UseAuthentication(); app.UseTrustedWorkspaceResolution(); app.UseAuthorization();
    app.MapCommunicationsEndpoints();
    app.MapLeadQualificationEndpoints();
    app.MapLeadCustomerConversionEndpoints();
    app.MapIdentityAuthEndpoints(); app.MapWorkspaceEndpoints(); app.MapAccessControlEndpoints(); app.MapStudioEndpoints();
    // Control operations exist ONLY in this loopback verifier, never in ApiHost/module routes.
    async Task Control(string action)
    {
        if (action.StartsWith("crm-", StringComparison.Ordinal)) { await CrmLinkVerification.Control(action, sql => Execute(sql)); return; }
        var sql = action switch {
            "grant-channels" => "INSERT INTO access.RoleCapabilities VALUES ('role_a','communications.channels.manage'); UPDATE workspace.BootstrapProjections SET AvailableProductSpacesJson='[\"crm\",\"studio\"]',ContextVersion=ContextVersion+1 WHERE WorkspaceId IN ('ws_com1_a','ws_com1_b')",
            "revoke-channels" => "DELETE FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability='communications.channels.manage'",
            "expire-channel-session" => "UPDATE iam.Sessions SET Status='Revoked' WHERE AccountId='account_a'",
            "channel-conflict" => "UPDATE communications.ChannelAccounts SET DisplayName='Browser concurrent change',ConfigurationVersion=ConfigurationVersion+1 WHERE DisplayName='Browser Gmail Sales'",
            "assign-b" => $"UPDATE communications.Conversations SET AssigneeMemberId='member_b',Version=Version+1 WHERE Id={id}",
            "assign-a" => $"UPDATE communications.Conversations SET AssigneeMemberId='member_a',Version=Version+1 WHERE Id={id}",
            "revoke" => "DELETE FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability='communications.read'",
            "grant" => "IF NOT EXISTS (SELECT 1 FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability='communications.read') INSERT INTO access.RoleCapabilities VALUES ('role_a','communications.read')",
            "hide" => "INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('browser_body_fls','ws_com1_a','role_a','communications','text','Hidden')",
            "show" => "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='browser_body_fls'",
            "grant-claim" => "INSERT INTO access.RoleCapabilities VALUES ('role_a','communications.claim'),('role_b','communications.claim'),('role_b','communications.queue.read')",
            "reassign-shell" => "UPDATE communications.Conversations SET AssigneeMemberId='member_b',Version=Version+1 WHERE LocalIdentity LIKE 'browser-claim-%' AND AssigneeMemberId='member_a'",
            "grant-transition" => "INSERT INTO access.RoleCapabilities VALUES ('role_a','communications.transition')",
            "revoke-transition" => "DELETE FROM access.RoleCapabilities WHERE RoleId='role_a' AND Capability='communications.transition'",
            "status-readonly" => "INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('browser_status_fls','ws_com1_a','role_a','communications','status','ReadOnly')",
            "status-hidden" => "INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('browser_status_fls','ws_com1_a','role_a','communications','status','Hidden')",
            "status-clear" => "DELETE FROM access.RoleFieldSecurity WHERE PolicyId='browser_status_fls'",
            _ => throw new InvalidOperationException("Unknown fixture control") };
        await Execute(sql);
    }
    app.MapPost("/__fixture/{action}", async (string action, Microsoft.AspNetCore.Http.HttpContext http) => {
        if (http.Request.Headers["X-Fixture-Key"] != controlKey) return Microsoft.AspNetCore.Http.Results.Unauthorized();
        if (action == "new-queue") { await NewClaimFixture("browser-claim"); return Microsoft.AspNetCore.Http.Results.NoContent(); }
        if (action == "telegram-browser-poll")
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>();
            var state = await db.TelegramPolling.AsNoTracking().SingleAsync(x => x.BotId == 1201 && x.WorkspaceId == "ws_com1_a" && x.Active);
            await scope.ServiceProvider.GetRequiredService<UnicoreCRM.Communications.Infrastructure.Telegram.TelegramInboundCoordinator>().PollAsync(state.WorkspaceId, state.AccountId);
            return Microsoft.AspNetCore.Http.Results.NoContent();
        }
        if (action == "trusted-inbound")
        {
            // Fixed synthetic bindings, admitted only by this verifier's secret loopback control.
            // No payload, Workspace, participant or trust assertion comes from the HTTP caller.
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>();
            var row = await db.Conversations.Where(x => x.WorkspaceId == "ws_com1_a" && x.AssigneeMemberId == "member_a"
                && x.LocalIdentity.StartsWith("browser-claim-") && x.Status == "CLOSED").OrderByDescending(x => x.Id).FirstAsync();
            var participant = await db.Participants.SingleAsync(x => x.WorkspaceId == row.WorkspaceId && x.AccountId == row.AccountId);
            await new UnicoreCRM.Communications.Application.LocalCommunicationsStore(db, new EphemeralDataProtectionProvider(), TimeProvider.System)
                .AppendAsync(row.WorkspaceId, row.Id, participant.Id, "browser-inbound-" + Guid.NewGuid().ToString("N"), "SYNTHETIC_TRUSTED_INBOUND", DateTimeOffset.UtcNow);
            return Microsoft.AspNetCore.Http.Results.NoContent();
        }
        if (!action.StartsWith("crm-", StringComparison.Ordinal) && action is not ("grant-channels" or "revoke-channels" or "expire-channel-session" or "channel-conflict" or "assign-a" or "assign-b" or "revoke" or "grant" or "hide" or "show" or "grant-claim" or "reassign-shell" or "grant-transition" or "revoke-transition" or "status-readonly" or "status-hidden" or "status-clear")) return Microsoft.AspNetCore.Http.Results.BadRequest();
        await Control(action); return Microsoft.AspNetCore.Http.Results.NoContent();
    });
    await app.StartAsync();
    var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    string Token(string actor) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("Com1Fixture", "Com1Fixture",
        [new Claim("sub", $"account_{actor}"), new Claim("member_id", $"member_{actor}"), new Claim("sid", $"session_{actor}")],
        DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(30), new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)));
    var tokens = actors.ToDictionary(actor => actor, Token);
    if (args.Contains("--activation-only", StringComparer.Ordinal) && connectionFixture is not null)
    {
        await Execute("INSERT INTO access.RoleCapabilities VALUES ('role_admin','communications.channels.manage'),('role_foreign','communications.channels.manage')");
        await connectionFixture.RunNormalActivationAsync(connection, key, tokens, sql => Execute(sql), sql => Scalar(sql), Check);
        Console.WriteLine($"ASSERTIONS {checks}");
        await app.StopAsync();
        return;
    }
    if (args.Contains("--custody-only", StringComparer.Ordinal))
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        await Execute("INSERT INTO access.RoleCapabilities VALUES ('role_admin','communications.channels.manage'),('role_foreign','communications.channels.manage')");
        await ApiHostCustodyVerification.Run(root, artifacts, database, connection, key, tokens, sql => Scalar(sql), Check);
        Console.WriteLine($"ASSERTIONS {checks}");
        await app.StopAsync();
        return;
    }
    using var http = new HttpClient { BaseAddress = new(url) };
    async Task<(HttpStatusCode Status, JsonElement Body)> Get(string path, string? actor = "a", string workspace = "ws_com1_a")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (actor is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[actor]);
        request.Headers.Add("X-Workspace-Id", workspace); request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
        using var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode) Check(response.Headers.CacheControl?.NoStore == true, "protected HTTP response forbids browser/proxy caching");
        var body = await response.Content.ReadAsStringAsync();
        Check(!body.Contains("Exception", StringComparison.Ordinal) && !body.Contains("SELECT", StringComparison.Ordinal), "HTTP does not expose raw SQL/exception diagnostics");
        return (response.StatusCode, JsonDocument.Parse(body).RootElement.Clone());
    }
    var path = $"/communications/conversations/{id}";
    Check((await Get(path)).Status == HttpStatusCode.OK, "assigned Agent A with explicit read grant reads detail under Workspace policy cap");
    Check((await Get(path, "b")).Status == HttpStatusCode.NotFound, "Agent B cannot read another assignee's conversation");
    Check((await Get(path + "/messages", "b")).Status == HttpStatusCode.NotFound, "Agent B cannot read another assignee's message bodies");
    foreach (var actor in new[] { "admin", "manager", "triage" })
        Check((await Get(path + "/messages", actor)).Status == HttpStatusCode.Forbidden, $"{actor} does not inherit protected content grant");
    Check((await Get(path, null)).Status == HttpStatusCode.Unauthorized, "unauthenticated request denied");
    Check((await Get(path, "b", "ws_com1_b")).Status == HttpStatusCode.Forbidden, "untrusted workspace header without membership rejected");
    Check((await Get($"/communications/conversations/{foreignId}")).Status == HttpStatusCode.NotFound, "foreign workspace id indistinguishable from missing");
    foreach (var actor in new[] { "a", "b", "triage" })
        Check((await Get($"/communications/conversations/{unassignedId}/messages", actor)).Status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, "unassigned body denied even for triage");
    Check((await Get($"/communications/conversations/{unassignedId}")).Status == HttpStatusCode.NotFound,
        "unassigned detail denied even with both read and triage capabilities");
    var queue = await Get("/communications/conversations?view=unassigned", "triage");
    Check(queue.Status == HttpStatusCode.OK && queue.Body.GetProperty("items").EnumerateArray().All(row => row.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "id", "lastActivityAt" })), "triage-only queue contains exactly minimal metadata");
    Check(!queue.Body.ToString().Contains("SYNTHETIC_") && !queue.Body.TryGetProperty("totalCount", out _), "queue omits body subject identity CRM and protected counts");
    Check((await Get("/communications/conversations?view=unassigned", "b")).Status == HttpStatusCode.Forbidden, "read grant alone cannot read unassigned queue");
    var list = await Get("/communications/conversations?limit=25");
    var items = list.Body.GetProperty("items").EnumerateArray().ToArray();
    Check(items.Length == 25 && items.All(row => row.GetProperty("id").GetString() != bId.ToString() && row.GetProperty("id").GetString() != unassignedId.ToString()), "SQL assignee scope applied before first page despite newer foreign-assignee/queue records");
    var cursor = list.Body.GetProperty("nextCursor").GetString()!;
    var next = await Get("/communications/conversations?limit=25&cursor=" + Uri.EscapeDataString(cursor));
    Check(next.Body.GetProperty("items").GetArrayLength() == 1, "assigned continuation returns remaining own record");
    Check((await Get("/communications/conversations?limit=25&cursor=" + Uri.EscapeDataString(cursor), "b")).Status == HttpStatusCode.BadRequest, "cursor cannot cross member authority");
    Check((await Get("/communications/conversations?limit=25&cursor=" + Uri.EscapeDataString(cursor), "a", "ws_com1_b")).Status == HttpStatusCode.BadRequest, "cursor cannot cross valid workspaces");
    Check((await Get("/communications/conversations?workspaceId=ws_com1_b")).Status == HttpStatusCode.BadRequest, "client workspace query is not accepted as authority");
    var messages = await Get(path + "/messages?limit=25");
    Check(messages.Body.GetProperty("items").GetArrayLength() == 25 && messages.Body.ToString().Contains("SYNTHETIC_BODY"), "authorized messages body and server page available");
    var messageCursor = messages.Body.GetProperty("nextCursor").GetString()!;
    Check((await Get(path + "/messages?limit=25&cursor=" + Uri.EscapeDataString(messageCursor))).Body.GetProperty("items").GetArrayLength() == 2, "message continuation works over SQL");
    await Execute("INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('body_fls','ws_com1_a','role_a','communications','text','Hidden')");
    var hidden = await Get(path + "/messages");
    Check(hidden.Status == HttpStatusCode.OK && hidden.Body.GetProperty("items").EnumerateArray().All(m => !m.TryGetProperty("text", out _)), "Message FLS removes text property from HTTP response");
    Check((await Get(path + "/messages?limit=25&cursor=" + Uri.EscapeDataString(messageCursor))).Status == HttpStatusCode.BadRequest, "cursor retires when effective field authority changes");
    await Execute("DELETE FROM access.RoleFieldSecurity WHERE PolicyId='body_fls'");
    await Execute("INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('required_fls','ws_com1_a','role_a','communications','id','Hidden')");
    Check((await Get(path)).Status == HttpStatusCode.Forbidden && (await Get("/communications/conversations")).Status == HttpStatusCode.Forbidden,
        "required metadata FLS fails closed for detail and list");
    await Execute("DELETE FROM access.RoleFieldSecurity WHERE PolicyId='required_fls'");
    await Execute("UPDATE access.RoleDataScopes SET Scope='Team' WHERE PolicyId='scope_a'");
    Check((await Get(path)).Status == HttpStatusCode.Forbidden, "unapproved Team content scope denied even for current assignee");
    await Execute("UPDATE access.RoleDataScopes SET Scope='Workspace' WHERE PolicyId='scope_a'");
    await Control("assign-b");
    Check((await Get(path + "/messages")).Status == HttpStatusCode.NotFound && (await Get(path, "b")).Status == HttpStatusCode.OK, "reassignment enforced on next request without token renewal");
    await Control("assign-a"); await Control("revoke");
    Check((await Get(path + "/messages")).Status == HttpStatusCode.Forbidden, "capability revocation enforced on next request with same JWT");
    await Control("grant");
    Check(Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM access.RecordAccessDecisions WHERE ResourceKey='communications'")) > 0, "real AccessControl writes scoped record authorization audit");

    async Task<long> NewClaimFixture(string label)
    {
        label += "-" + Guid.NewGuid().ToString("N");
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunicationsDbContext>();
        var account = await db.Accounts.SingleAsync(x => x.WorkspaceId == "ws_com1_a" && x.LocalIdentity == "fixture");
        var row = new Conversation(account, label, DateTimeOffset.UtcNow.AddDays(-10));
        db.Add(row); await db.SaveChangesAsync();
        var participant = await db.Participants.SingleAsync(x => x.AccountId == account.Id);
        db.Messages.Add(row.Append(participant, label + "-message", "SYNTHETIC_CLAIM_BODY", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        return row.Id;
    }
    async Task<(HttpStatusCode Status, string Body)> ClaimConversation(long target, string? actor = "a", string workspace = "ws_com1_a", string? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/communications/conversations/{target}/claim");
        if (actor is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[actor]);
        request.Headers.Add("X-Workspace-Id", workspace);
        request.Headers.Add("X-Request-Id", Guid.NewGuid().ToString("N"));
        request.Headers.Add("X-Correlation-Id", "claim-correlation-fixture");
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Check(!text.Contains("SYNTHETIC_") && !text.Contains("SELECT") && !text.Contains("Exception"), "claim HTTP response omits protected content and diagnostics");
        return (response.StatusCode, text);
    }
    var claimId = await NewClaimFixture("claim-race");
    Check((await ClaimConversation(claimId, null)).Status == HttpStatusCode.Unauthorized, "unauthenticated Claim denied");
    Check((await ClaimConversation(claimId, "triage")).Status == HttpStatusCode.Forbidden, "queue-read-only cannot Claim");
    Check((await ClaimConversation(claimId)).Status == HttpStatusCode.Forbidden, "read and queue without Claim capability denied");
    await Execute("INSERT INTO access.RoleCapabilities VALUES ('role_a','communications.claim'),('role_b','communications.claim'),('role_b','communications.queue.read'),('role_triage','communications.claim')");
    Check((await ClaimConversation(claimId, "triage")).Status == HttpStatusCode.Forbidden, "Claim plus queue without content read denied");
    await Execute("DELETE FROM access.RoleCapabilities WHERE RoleId='role_b' AND Capability='communications.queue.read'");
    Check((await ClaimConversation(claimId, "b")).Status == HttpStatusCode.Forbidden, "Claim plus content read without queue denied");
    await Execute("INSERT INTO access.RoleCapabilities VALUES ('role_b','communications.queue.read')");
    Check((await ClaimConversation(claimId, "b", "ws_com1_b")).Status == HttpStatusCode.Forbidden, "Claim untrusted Workspace denied");
    Check((await ClaimConversation(foreignId)).Status == HttpStatusCode.NotFound, "Claim cross Workspace id returns 404");
    Check((await ClaimConversation(claimId, body: "{\"assigneeMemberId\":\"member_b\"}")).Status == HttpStatusCode.BadRequest, "Claim rejects client member authority body");
    await Execute("UPDATE workspace.Memberships SET Status='Suspended' WHERE MembershipId='membership_a'");
    Check((await ClaimConversation(claimId)).Status == HttpStatusCode.Forbidden, "inactive member cannot Claim with old JWT");
    await Execute("UPDATE workspace.Memberships SET Status='Active' WHERE MembershipId='membership_a'");
    await Execute("UPDATE access.RoleDataScopes SET Scope='Team' WHERE PolicyId='scope_a'");
    Check((await ClaimConversation(claimId)).Status == HttpStatusCode.Forbidden, "Claim unsupported content scope denied");
    await Execute("UPDATE access.RoleDataScopes SET Scope='Workspace' WHERE PolicyId='scope_a'");
    await Execute("INSERT INTO access.RoleFieldSecurity (PolicyId,WorkspaceId,RoleId,ResourceKey,FieldKey,Access) VALUES ('claim_owner_fls','ws_com1_a','role_a','communications.queue','ownerId','ReadOnly')");
    Check((await ClaimConversation(claimId)).Status == HttpStatusCode.Forbidden, "Claim respects ownership field write restriction");
    await Execute("DELETE FROM access.RoleFieldSecurity WHERE PolicyId='claim_owner_fls'");
    var race = await Task.WhenAll(ClaimConversation(claimId), ClaimConversation(claimId, "b"));
    Check(race.Count(x => x.Status == HttpStatusCode.OK) == 1 && race.Count(x => x.Status == HttpStatusCode.Conflict) == 1,
        "two authenticated actors racing Claim yield one commit and one 409");
    var winner = race[0].Status == HttpStatusCode.OK ? "a" : "b";
    var loser = winner == "a" ? "b" : "a";
    Check(Convert.ToInt64(await Scalar($"SELECT Version FROM communications.Conversations WHERE Id={claimId}")) == 2, "Claim increments existing append version once");
    Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM communications.AuditRecords WHERE ConversationId={claimId} AND Operation='CONVERSATION_CLAIMED'")) == 1, "race records one Claim audit");
    var replay = await ClaimConversation(claimId, winner);
    Check(replay.Status == HttpStatusCode.OK && JsonDocument.Parse(replay.Body).RootElement.GetProperty("replayed").GetBoolean(), "same-actor retry succeeds as replay");
    Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM communications.AuditRecords WHERE ConversationId={claimId} AND Operation='CONVERSATION_CLAIMED'")) == 1
        && Convert.ToInt64(await Scalar($"SELECT Version FROM communications.Conversations WHERE Id={claimId}")) == 2, "retry creates no transition audit or version increment");
    Check((await Get($"/communications/conversations/{claimId}/messages", winner)).Body.ToString().Contains("SYNTHETIC_CLAIM_BODY"), "Claim winner reads through existing authorized Message API");
    Check((await Get($"/communications/conversations/{claimId}/messages", loser)).Status == HttpStatusCode.NotFound, "Claim loser cannot read protected messages");
    Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM communications.AuditRecords WHERE ConversationId={claimId} AND ActorMemberId='member_{winner}' AND PreviousAssigneeMemberId IS NULL AND NewAssigneeMemberId='member_{winner}' AND MessageId IS NULL AND CorrelationId='claim-correlation-fixture'")) == 1,
        "audit contains actor previous/new assignee operation time and correlation without body");
    var rollbackId = await NewClaimFixture("claim-rollback");
    await Execute("CREATE TRIGGER communications.RejectFixtureClaimAudit ON communications.AuditRecords AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Operation='CONVERSATION_CLAIMED') THROW 51000, 'Fixture audit write refused', 1; END");
    Check((await ClaimConversation(rollbackId)).Status == HttpStatusCode.InternalServerError, "unexpected SQL audit failure is not reported as success");
    await Execute("DROP TRIGGER communications.RejectFixtureClaimAudit");
    Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM communications.Conversations WHERE Id={rollbackId} AND AssigneeMemberId IS NULL AND Version=1")) == 1
        && Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM communications.AuditRecords WHERE ConversationId={rollbackId}")) == 0,
        "audit SQL failure rolls back ownership version and audit atomically");
    Check(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM communications.Messages WHERE ConversationId={rollbackId}")) == 1, "rollback preserves messages");
    Check((await ClaimConversation(rollbackId)).Status == HttpStatusCode.OK, "Claim succeeds after storage failure is removed");
    // Leave original COM-1 queue and assigned page unchanged for read/browser regressions.
    await Execute("DELETE FROM access.RoleCapabilities WHERE Capability='communications.claim'; DELETE FROM access.RoleCapabilities WHERE RoleId='role_b' AND Capability='communications.queue.read'");
    await Execute($"DELETE FROM communications.AuditRecords WHERE ConversationId IN ({claimId},{rollbackId}); DELETE FROM communications.Messages WHERE ConversationId IN ({claimId},{rollbackId}); DELETE FROM communications.Conversations WHERE Id IN ({claimId},{rollbackId})");

    if (!args.Contains("--connections-only", StringComparer.Ordinal))
    {
        await LifecycleVerification.Run(app.Services, http, tokens, sql => Execute(sql), sql => Scalar(sql), Check);
        await CrmLinkVerification.Run(app.Services, http, tokens, sql => Execute(sql), sql => Scalar(sql), Check);
    }
    await ChannelFoundationVerification.Run(http, tokens, sql => Execute(sql), sql => Scalar(sql), Check);
    long? telegramId = null;
    if (args.Contains("--telegram", StringComparer.Ordinal))
        telegramId = await TelegramInboundVerification.Run(connection, http, tokens, sql => Execute(sql), sql => Scalar(sql), Check);
    if (connectionFixture is not null)
        await connectionFixture.RunAsync(app.Services, http, tokens, sql => Execute(sql), sql => Scalar(sql), Check);

    if (args.Contains("--apihost-custody", StringComparer.Ordinal))
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Local DPAPI process verification requires Windows");
        await ApiHostCustodyVerification.Run(root, artifacts, database, connection, key, tokens, sql => Scalar(sql), Check);
    }
    if (args.Contains("--activation", StringComparer.Ordinal) && connectionFixture is not null)
        await connectionFixture.RunNormalActivationAsync(connection, key, tokens, sql => Execute(sql), sql => Scalar(sql), Check);

    if (!args.Contains("--skip-browser", StringComparer.Ordinal))
    {
        if (args.Contains("--activation", StringComparer.Ordinal)) app.Services.GetRequiredService<UnicoreCRM.Communications.Infrastructure.Telegram.TelegramPollingOptions>().Enabled = true;
        var processInfo = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(root, "frontend/unicorecrm-web"), UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        processInfo.ArgumentList.Add("--import"); processInfo.ArgumentList.Add("tsx");
        processInfo.ArgumentList.Add(args.Contains("--shell", StringComparer.Ordinal) ? "tests/contract/communications-shell-browser.mts" : "tests/contract/communications-connected-browser.mts");
        processInfo.Environment["COM_FIXTURE"] = JsonSerializer.Serialize(new { url, id = id.ToString(), unassignedId = unassignedId.ToString(),
            controlKey, tokens, telegramId });
        if (args.Contains("--foundation", StringComparer.Ordinal)) processInfo.Environment["COMMUNICATIONS_FOUNDATION_VERIFY"] = "1";
        if (connectionFixture is not null) processInfo.Environment["COMMUNICATIONS_CREDENTIAL_VERIFY"] = "1";
        if (args.Contains("--activation", StringComparer.Ordinal)) processInfo.Environment["COMMUNICATIONS_ACTIVATION_VERIFY"] = "1";
        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Browser verifier could not start");
        var browserOutput = process.StandardOutput.ReadToEndAsync();
        var browserErrors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(args.Contains("--foundation", StringComparer.Ordinal) ? 5 : 3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Console.Write(await browserOutput);
        Console.Error.Write(await browserErrors);
        Check(process.ExitCode == 0, "real browser connected adapter/controller/timeline acceptance");
    }
    Console.WriteLine($"ASSERTIONS {checks}");
    await app.StopAsync();
}
finally
{
    if (created)
    {
        SqlConnection.ClearAllPools();
        await Execute($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]", masterString);
        Check(await Scalar($"SELECT DB_ID(N'{database}')", masterString) is DBNull, "owned SQL database cleanup verified");
        Check(!Directory.EnumerateFiles(files).Any(), "owned SQL files removed; user databases untouched");
    }
    Console.WriteLine($"DATABASE {database}");
}
