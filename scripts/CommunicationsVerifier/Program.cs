using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Communications;
using UnicoreCRM.Communications.Application;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Persistence;

var assertions = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    assertions++;
    Console.WriteLine($"PASS {name}");
}
async Task Reject<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); }
    catch (T) { Check(true, name); return; }
    throw new InvalidOperationException($"Expected rejection: {name}");
}

// No application configuration is read. A random task-owned DB, explicit files inside task artifacts.
var database = $"UnicoreCRM_COM2C_CORE_{Guid.NewGuid():N}";
var artifactRoot = Environment.GetEnvironmentVariable("COMMUNICATIONS_VERIFICATION_ARTIFACTS")
    ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../review-artifacts/communications-com2c-manual-crm-linking-20261016"));
var directory = Path.Combine(artifactRoot, "sql-disposable", database);
Directory.CreateDirectory(directory);
var masterConnection = "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=True;TrustServerCertificate=True;Pooling=False";
var taskConnection = new SqlConnectionStringBuilder(masterConnection) { InitialCatalog = database }.ConnectionString;
var options = new DbContextOptionsBuilder<CommunicationsDbContext>().UseSqlServer(taskConnection,
    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "communications")).Options;
CommunicationsDbContext Context() => new(options);
await using var master = new SqlConnection(masterConnection);
await master.OpenAsync();
async Task<object?> Scalar(string sql)
{
    await using var command = new SqlCommand(sql, master);
    return await command.ExecuteScalarAsync();
}
async Task Execute(string sql)
{
    await using var command = new SqlCommand(sql, master);
    await command.ExecuteNonQueryAsync();
}
Check(await Scalar($"SELECT DB_ID(N'{database}')") is DBNull, "random disposable database does not exist before creation");
var created = false;
try
{
    var dataPath = Path.Combine(directory, "communications.mdf").Replace("'", "''");
    var logPath = Path.Combine(directory, "communications_log.ldf").Replace("'", "''");
    await Execute($"CREATE DATABASE [{database}] ON PRIMARY (NAME=N'{database}', FILENAME=N'{dataPath}') LOG ON (NAME=N'{database}_log', FILENAME=N'{logPath}')");
    created = true;
    await using (var db = Context())
    {
        await db.Database.MigrateAsync();
        Check(!db.Database.HasPendingModelChanges(), "migration snapshot matches model");
        Check((await db.Database.GetAppliedMigrationsAsync()).Count() == 9, "all Communications migrations applied on SQL Server");
        Check(db.Model.GetEntityTypes().All(e => e.GetSchema() == "communications"), "schema ownership limited to communications");
        Check(db.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()).All(f => f.PrincipalEntityType.GetSchema() == "communications"), "no cross-module foreign keys");
    }

    var protection = new EphemeralDataProtectionProvider();
    var at = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
    var accountA = new ChannelAccount("ws_com1_a", "local-account");
    var accountB = new ChannelAccount("ws_com1_b", "local-account");
    var accountA2 = new ChannelAccount("ws_com1_a", "second-local-account");
    var participantA = new ExternalParticipant(accountA, "synthetic-person");
    var participantB = new ExternalParticipant(accountB, "synthetic-person");
    var participantA2 = new ExternalParticipant(accountA2, "synthetic-person");
    var conversations = Enumerable.Range(1, 5).Select(i => new Conversation(accountA, $"conversation-{i}", at)).ToArray();
    var foreign = new Conversation(accountB, "conversation-1", at.AddDays(1));
    await using (var db = Context())
    {
        db.AddRange(accountA, accountB, accountA2, participantA, participantB, participantA2, foreign);
        db.AddRange(conversations);
        await db.SaveChangesAsync();
    }
    var id = conversations[0].Id;
    await using (var db = Context())
    {
        var store = new LocalCommunicationsStore(db, protection, TimeProvider.System);
        for (var i = 1; i <= 5; i++) await store.AppendAsync("ws_com1_a", id, participantA.Id, $"message-{i}", $"Synthetic text {i}", at.AddMinutes(6 - i));
        Check((await store.GetAsync("ws_com1_a", id)) is { Version: 5, LastActivityAt: var activity } && activity == at.AddMinutes(5), "messages persist with atomic version and nondecreasing activity");
        Check(await db.Audit.CountAsync() == 5, "one durable metadata audit per append");
        Check((await db.Audit.ToListAsync()).All(a => a.WorkspaceId == "ws_com1_a" && a.ConversationId == id), "audit scope and resource binding");
        Check(await store.GetAsync("ws_com1_b", id) is null, "cross-workspace detail withheld");
        Check(await store.GetAsync("WS_COM1_A", id) is null && (await store.ListAsync("WS_COM1_A", 2)).Items.Count == 0,
            "SQL workspace comparisons preserve exact internal identity");
        Check((await store.MessagesAsync("ws_com1_b", id, 2)).Items.Count == 0, "cross-workspace timeline withheld");

        var seen = new List<long>();
        string? cursor = null;
        do
        {
            var page = await store.ListAsync("ws_com1_a", 2, cursor);
            seen.AddRange(page.Items.Select(x => x.Id));
            cursor = page.NextCursor;
        } while (cursor is not null);
        var expected = new[] { id }.Concat(conversations.Skip(1).Select(c => c.Id).OrderDescending()).ToArray();
        Check(seen.SequenceEqual(expected) && seen.Distinct().Count() == 5, "conversation keyset pages use activity then stable descending id");
        var first = await store.ListAsync("ws_com1_a", 2);
        await Reject<ArgumentException>(() => store.ListAsync("ws_com1_b", 2, first.NextCursor), "cursor workspace mismatch refused");
        await Reject<ArgumentException>(() => store.ListAsync("ws_com1_a", 3, first.NextCursor), "cursor page-size mismatch refused");
        await Reject<ArgumentException>(() => store.ListAsync("ws_com1_a", 2, first.NextCursor + "tamper"), "cursor tampering refused without raw diagnostics");
        await Reject<ArgumentException>(() => store.ListAsync("ws_com1_a", 101), "unbounded page size refused");
        await Reject<ArgumentException>(() => store.ListAsync(" ", 2), "empty workspace rejected");
        var messagePage = await store.MessagesAsync("ws_com1_a", id, 2);
        await Reject<ArgumentException>(() => store.MessagesAsync("ws_com1_a", conversations[1].Id, 2, messagePage.NextCursor), "cursor conversation mismatch refused");
        await Reject<ArgumentException>(() => store.MessagesAsync("ws_com1_a", id, 2, first.NextCursor), "cursor stream mismatch refused");
        var sequences = new List<long>();
        cursor = null;
        do
        {
            var page = await store.MessagesAsync("ws_com1_a", id, 2, cursor);
            sequences.AddRange(page.Items.Select(m => m.Sequence));
            Check(page.Items.All(m => m.ConversationId == id), "message page binds to conversation");
            cursor = page.NextCursor;
        } while (cursor is not null);
        Check(sequences.SequenceEqual(new long[] { 1, 2, 3, 4, 5 }), "message pagination follows durable append order including out-of-order timestamps");
        await Reject<InvalidOperationException>(() => store.AppendAsync("ws_com1_a", id, participantB.Id, "foreign", "synthetic", at), "foreign participant rejected");
        await Reject<InvalidOperationException>(() => store.AppendAsync("ws_com1_a", id, participantA2.Id, "other-account", "synthetic", at), "same-workspace incorrect account participant rejected");
        await Reject<InvalidOperationException>(() => store.AppendAsync("ws_com1_a", foreign.Id, participantA.Id, "foreign-conversation", "synthetic", at), "foreign conversation ingestion rejected");
        await Reject<ArgumentException>(() => store.AppendAsync("ws_com1_a", id, participantA.Id, "empty", "", at), "empty content rejected before mutation");
    }
    await using (var db = Context())
    {
        var store = new LocalCommunicationsStore(db, protection, TimeProvider.System);
        await Reject<InvalidOperationException>(() => store.AppendAsync("ws_com1_a", id, participantA.Id, "message-1", "duplicate", at), "conflicting account-scoped replay rejected before aggregate mutation");
    }
    await using (var db = Context())
    {
        Check((await db.Conversations.SingleAsync(c => c.Id == id)).Version == 5 && await db.Audit.CountAsync() == 5 && await db.Messages.CountAsync() == 5,
            "failed duplicate rolls back message, version and audit");
    }
    foreach (var duplicate in new object[] { new ChannelAccount("ws_com1_a", "local-account"), new ExternalParticipant(accountA, "synthetic-person"), new Conversation(accountA, "conversation-1", at) })
    {
        await using var db = Context();
        db.Add(duplicate);
        await Reject<DbUpdateException>(() => db.SaveChangesAsync(), $"SQL duplicate {duplicate.GetType().Name} rejected");
    }
    await using (var db = Context())
    {
        var bad = new ExternalParticipant(accountA, "wrong-workspace");
        db.Add(bad);
        db.Entry(bad).Property(x => x.WorkspaceId).CurrentValue = "ws_com1_b";
        await Reject<DbUpdateException>(() => db.SaveChangesAsync(), "composite SQL foreign key refuses incorrect workspace account binding");
    }
    await using (var first = Context())
    await using (var second = Context())
    {
        var stale = await second.Conversations.SingleAsync(c => c.Id == id);
        await new LocalCommunicationsStore(first, protection, TimeProvider.System).AppendAsync("ws_com1_a", id, participantA.Id, "winner", "synthetic winner", at);
        second.Messages.Add(stale.Append(participantA, "loser", "synthetic stale", at));
        await Reject<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(), "stale aggregate version rejected atomically");
    }
    await using (var db = Context())
    {
        Check(await db.Messages.CountAsync() == 6 && await db.Audit.CountAsync() == 6, "stale append has no partial message commit");
        await new LocalCommunicationsStore(db, protection, TimeProvider.System).AppendAsync("ws_com1_a", id, participantA.Id, "whitespace", "  synthetic\n text  ", at);
        Check((await db.Messages.SingleAsync(m => m.LocalIdentity == "whitespace")).Text == "  synthetic\n text  ", "message text whitespace preserved exactly");
        Check((await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id=OBJECT_ID('communications.Messages') AND is_unique=1 AND name='IX_Messages_WorkspaceId_ConversationId_Sequence'").SingleAsync()) == 1,
            "actual SQL message paging unique index exists");
        await db.Database.MigrateAsync("0");
        Check(!await db.Database.SqlQueryRaw<string>("SELECT [name] AS [Value] FROM sys.tables WHERE schema_id = SCHEMA_ID('communications') AND [name] <> '__EFMigrationsHistory'").AnyAsync(), "down migration removes module tables");
        await db.Database.MigrateAsync();
        Check(!db.Database.HasPendingModelChanges(), "migration reapplies cleanly");
    }

    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:UnicoreCRM"] = taskConnection });
    builder.Services.AddCommunicationsModule(builder.Configuration);
    Check(typeof(LocalCommunicationsStore).IsNotPublic && builder.Services.Any(s => s.ServiceType == typeof(LocalCommunicationsStore)),
        "shared ingestion is internal and registered only for the admitted Telegram coordinator");
    Check(!builder.Services.Where(s => s.ServiceType == typeof(UnicoreCRM.Communications.Infrastructure.Telegram.TelegramPollingOptions))
        .Select(s => (UnicoreCRM.Communications.Infrastructure.Telegram.TelegramPollingOptions)s.ImplementationInstance!).Single().Enabled,
        "Telegram worker disabled by default without configuration or credentials");
    await using (var app = builder.Build())
    {
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient { BaseAddress = new(address) };
        foreach (var path in new[] { "/communications/conversations", $"/communications/conversations/{id}", $"/communications/conversations/{id}/messages" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-Workspace-Id", "ws_com1_a");
            request.Headers.Add("Authorization", "Bearer untrusted-claims");
            using var response = await http.SendAsync(request);
            Check(response.StatusCode == System.Net.HttpStatusCode.NotFound && string.IsNullOrEmpty(await response.Content.ReadAsStringAsync()), "public protected read unavailable even with claimed workspace/authority");
        }
        await app.StopAsync();
    }
    var approvedReadTypes = new[] { "ChannelProviderDocument", "ChannelAccountDocument", "ChannelSummary", "ChannelAccountPage", "ChannelMutationDocument", "CrmLinkTargetDocument", "ConversationCrmContextDocument", "ConversationLinkMutationDocument", "ConversationTransitionDocument", "ConversationClaimDocument", "CommunicationsModule", "CommunicationsEndpoints", "ConversationDocument", "QueueConversationDocument", "ConversationMessageDocument", "CommunicationsPage`1" };
    Check(typeof(CommunicationsModule).Assembly.GetExportedTypes().All(t => approvedReadTypes.Contains(t.Name, StringComparer.Ordinal) || t.Name == "TelegramConnectionDocument"
        || typeof(Microsoft.EntityFrameworkCore.Migrations.Migration).IsAssignableFrom(t)),
        "public surface limited to admitted Communications contracts registration and migrations; no ingestion contract");
    Console.WriteLine($"ASSERTIONS {assertions}");
}
finally
{
    if (created)
    {
        // Name generated above and creation verified by this process; never accept an external DB name.
        SqlConnection.ClearAllPools();
        await Execute($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
        Check(await Scalar($"SELECT DB_ID(N'{database}')") is DBNull, "task-owned database cleanup verified");
        Check(!File.Exists(Path.Combine(directory, "communications.mdf")) && !File.Exists(Path.Combine(directory, "communications_log.ldf")), "task-owned database files removed by SQL Server");
    }
    Console.WriteLine($"DATABASE {database}; FILE_DIRECTORY {directory}");
}
