using System.Reflection;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Application.ReadContactFollowUp;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Operations.Tasks.Infrastructure.Persistence;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS {name}");
}
var evaluator = new Evaluator();
var resolver = new ContactFollowUpReadAuthorityResolver(new TaskAuthorization(evaluator));
async Task<ContactFollowUpAuthorityResult> Resolve() => await resolver.ResolveAsync("request", "correlation", default);
Check((await Resolve()).IsAvailable && evaluator.Calls == 1, "single canonical tasks.read decision");
foreach (var field in new[] { "recordRef", "dueAt", "assigneeId", "status", "archivedAt" })
{
    evaluator.Hidden = field;
    Check(!(await Resolve()).IsAvailable, $"withheld {field} refuses derived disclosure");
}
evaluator.Hidden = null;
evaluator.OmittedField = "recordRef";
Check(!(await Resolve()).IsAvailable, "missing required field decision fails closed");
evaluator.OmittedField = null;
evaluator.Unenforceable = ["dueAt"];
Check(!(await Resolve()).IsAvailable, "canonical unenforceable representation denied");
evaluator.Unenforceable = [];
evaluator.Allowed = false;
Check(!(await Resolve()).IsAvailable, "missing capability denied");
evaluator.Allowed = true;
evaluator.WorkspaceMismatch = true;
Check((await Resolve()).ErrorCode == "WORKSPACE_MISMATCH", "workspace mismatch denied");
evaluator.WorkspaceMismatch = false;
foreach (var scope in new[] { RecordAccessScopeFilter.Denied, RecordAccessScopeFilter.NotEvaluated })
{
    evaluator.Scope = scope;
    Check(!(await Resolve()).IsAvailable, $"unsupported scope {scope} refused");
}
evaluator.Scope = RecordAccessScopeFilter.OwnedByMember;
evaluator.Owner = null;
Check(!(await Resolve()).IsAvailable, "OWN without assignee authority refused");
evaluator.Owner = "member_a";
var own = (await Resolve()).Authority!;
evaluator.Scope = RecordAccessScopeFilter.Workspace;
var workspace = (await Resolve()).Authority!;

async Task<bool> ActivityReachable()
{
    var authorization = await evaluator.AuthorizeResourceAsync("tasks", "tasks.read", TaskFieldSecurity.FieldKeys,
        RecordAccessRepresentation.Full, new("request", "correlation"), default);
    return TaskActivitySecurity.IsReachable(new TaskAccess(authorization.TrustedWorkspace!, authorization));
}
evaluator.FieldMode = RecordFieldEnforcement.ReadWrite;
Check(await ActivityReachable(), "existing unrestricted WORKSPACE Activity gate remains reachable");
foreach (var scope in new[] { RecordAccessScopeFilter.OwnedByMember, RecordAccessScopeFilter.Denied, RecordAccessScopeFilter.NotEvaluated })
{
    evaluator.Scope = scope;
    Check(!await ActivityReachable(), $"existing Activity scope gate refuses {scope}");
}
evaluator.Scope = RecordAccessScopeFilter.Workspace;
foreach (var mode in new[] { RecordFieldEnforcement.ReadOnly, RecordFieldEnforcement.Withheld })
{
    evaluator.FieldMode = mode;
    Check(!await ActivityReachable(), $"existing Activity field restriction gate refuses {mode}");
}
evaluator.FieldMode = RecordFieldEnforcement.ReadOnly;

// Only this newly created disposable database is mutated/dropped. No application config is read.
var database = $"UnicoreCRM_ContactFollowUpVerifier_{Guid.NewGuid():N}";
var server = Environment.GetEnvironmentVariable("CONTACT_FOLLOW_UP_SQL_SERVER") ?? "(localdb)\\MSSQLLocalDB";
var connection = $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True";
var options = new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(connection,
    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "tasks")).Options;
await using var tasks = new TasksDbContext(options);
try
{
    await tasks.Database.GetServiceMigrationToCurrent();
    Check(!tasks.Database.HasPendingModelChanges(), "current migration snapshot matches model");
    var due = new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
    TaskItem Add(string workspaceId = "ws_a", string member = "member_a", int days = 3,
        string module = "contacts", string? record = "contact_a")
    {
        var task = new TaskItem(workspaceId, "follow up", null, TaskPriority.Normal, member,
            due.AddDays(days), new("CONTACT", "relationship_only", module, record, null, null, null, null), null, due);
        tasks.Tasks.Add(task);
        return task;
    }
    Add(); Add(days: 4); Add(member: "member_b", days: 1); Add(workspaceId: "ws_b", days: -4);
    Add(days: -2).Complete("done", due);
    Add(days: -2).Cancel("cancel", due);
    Add(days: -2).Archive("archive", due);
    Add(days: -2, module: "leads"); Add(days: -2, record: null);
    Add(days: -2, record: ""); Add(days: 5, record: "contact_b");
    await tasks.SaveChangesAsync();
    await using var read = new ProjectionContext(new DbContextOptionsBuilder<ProjectionContext>().UseSqlServer(connection).Options);
    var sql = own.Compose(read.Rows).ToQueryString();
    Console.WriteLine("OWN COMPOSE SQL:\n" + sql);
    Check(sql.Contains("ContactFollowUpReadProjection") && !sql.Contains("[tasks].[Tasks]") &&
        sql.Contains("GROUP BY") && sql.Contains("AssigneeId"), "SQL composition uses owner view with workspace/assignee predicates and aggregate");
    var ownRows = await own.Compose(read.Rows).ToArrayAsync();
    Check(ownRows.Length == 2 && ownRows.Single(row => row.ContactId == "contact_a").NextFollowUpAt == due.AddDays(3),
        "OWN excludes other assignees before minimum; terminal/archived/foreign/non-Contact tasks excluded");
    var all = await workspace.Compose(read.Rows).ToArrayAsync();
    Check(all.Length == 2 && all.Single(row => row.ContactId == "contact_a").NextFollowUpAt == due.AddDays(1),
        "WORKSPACE minimizes eligible assignees and isolates workspace");
    Check(!all.Any(row => row.ContactId == "contact_missing"), "no eligible tasks yields no row for nullable left join");
    var contacts = read.Database.SqlQueryRaw<ContactFixture>("""
        SELECT N'ws_a' AS [WorkspaceId], N'contact_a' AS [ContactId]
        UNION ALL SELECT N'ws_a', N'contact_b'
        UNION ALL SELECT N'ws_a', N'contact_missing'
        """);
    var joined = from contact in contacts
                 join next in own.Compose(read.Rows)
                     on new { contact.WorkspaceId, contact.ContactId } equals new { next.WorkspaceId, next.ContactId } into followUps
                 from next in followUps.DefaultIfEmpty()
                 select new { contact.ContactId, NextFollowUpAt = (DateTimeOffset?)next.NextFollowUpAt };
    var page = await joined.OrderBy(row => row.NextFollowUpAt == null).ThenBy(row => row.NextFollowUpAt)
        .ThenBy(row => row.ContactId).Take(3).ToArrayAsync();
    Check(page.Length == 3 && page[0].ContactId == "contact_a" && page[2].NextFollowUpAt is null,
        "single SQL left join supports bounded sort with null last and no-task null");
    Check(await joined.CountAsync(row => row.NextFollowUpAt < due.AddDays(4)) == 1,
        "derived filter/count composes in SQL before paging");
    Check(await tasks.Tasks.CountAsync() == 11, "view migration and reads preserve tasks");
}
finally
{
    await tasks.Database.EnsureDeletedAsync();
}

sealed class ProjectionContext(DbContextOptions<ProjectionContext> options) : DbContext(options)
{
    public DbSet<ContactFollowUpProjectionRow> Rows => Set<ContactFollowUpProjectionRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<ContactFollowUpProjectionRow>(entity =>
    {
        entity.HasNoKey();
        entity.ToView(ContactFollowUpProjectionRow.ViewName, ContactFollowUpProjectionRow.Schema);
        entity.Property(row => row.WorkspaceId).HasMaxLength(128);
        entity.Property(row => row.ContactId).HasMaxLength(128);
        entity.Property(row => row.AssigneeId).HasMaxLength(128);
    });
}

sealed class ContactFixture
{
    public string WorkspaceId { get; set; } = null!;
    public string ContactId { get; set; } = null!;
}

sealed class Evaluator : IRecordAccessEvaluator
{
    public bool Allowed = true;
    public bool WorkspaceMismatch;
    public string? Hidden;
    public string? OmittedField;
    public string[] Unenforceable = [];
    public RecordFieldEnforcement FieldMode = RecordFieldEnforcement.ReadOnly;
    public RecordAccessScopeFilter Scope = RecordAccessScopeFilter.Workspace;
    public string? Owner = "member_a";
    public int Calls;
    public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resourceKey, string requiredCapability,
        IReadOnlyList<string>? requestedFields, RecordAccessRepresentation representation, RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        Calls++;
        if (resourceKey != "tasks" || requiredCapability != "tasks.read") throw new InvalidOperationException("Wrong authority");
        var constructor = typeof(RecordAccessAuthorization).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return Task.FromResult((RecordAccessAuthorization)constructor.Invoke([
            Allowed, WorkspaceMismatch ? "WORKSPACE_MISMATCH" : "AUTHORIZED",
            WorkspaceMismatch ? null : new TrustedWorkspaceContext("ws_a", "account", "member_a", "membership"),
            Scope, Owner, Scope.ToString(),
            requestedFields!.Where(field => field != OmittedField).ToDictionary(field => field, field => field == Hidden ? RecordFieldEnforcement.Withheld : FieldMode),
            Unenforceable, Allowed ? new[] { "tasks.read" } : Array.Empty<string>(), "policy", resourceKey, requiredCapability, Allowed, false, false]));
    }
    public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization authorization, string recordId,
        RecordAccessFacts facts, string enforcementPoint, RecordAccessRequestContext requestContext, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Projection must not perform per-task decisions");
}

static class MigrationVerification
{
    public static async Task GetServiceMigrationToCurrent(this Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade database)
    {
        // Current upgrade first, then the clean-install path with the new migration.
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(database);
        await migrator.MigrateAsync("20261001090000_LeadHandoverTaskIndex");
        var context = (TasksDbContext)Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
            .GetService<Microsoft.EntityFrameworkCore.Infrastructure.ICurrentDbContext>(database).Context;
        var now = DateTimeOffset.UtcNow;
        var existing = new TaskItem("ws_a", "existing", null, TaskPriority.Normal, "member_a", now,
            new(null, null, "contacts", "existing_contact", null, null, null, null), null, now);
        context.Tasks.Add(existing);
        await context.SaveChangesAsync();
        await database.MigrateAsync();
        if (await context.Tasks.CountAsync() != 1) throw new InvalidOperationException("Upgrade changed existing task data");
        Console.WriteLine("PASS current baseline upgrade");
        await migrator.MigrateAsync("20261001090000_LeadHandoverTaskIndex");
        await database.MigrateAsync();
        Console.WriteLine("PASS view rollback/reapply");
        await database.EnsureDeletedAsync();
        await database.MigrateAsync();
        Console.WriteLine("PASS clean full migration chain");
    }
}
