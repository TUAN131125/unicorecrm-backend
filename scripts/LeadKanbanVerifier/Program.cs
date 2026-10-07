using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Application.ListLeadKanbanColumn;
using UnicoreCRM.Crm.Leads.Infrastructure.Persistence;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

// Only this newly allocated database may be removed by this verifier.
var database = "UnicoreCRM_LeadKanban_" + Guid.NewGuid().ToString("N");
var options = new DbContextOptionsBuilder<LeadsDbContext>()
    .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;TrustServerCertificate=True").Options;
await using var db = new LeadsDbContext(options);
var now = DateTimeOffset.Parse("2026-10-06T10:00:00Z");
var protection = new EphemeralDataProtectionProvider();
var evaluator = new Evaluator();
var persistence = new EfLeadsPersistence(db);
var handler = new Handler(new LeadAuthorization(evaluator), persistence, persistence, protection, TimeProvider.System);
try
{
    await db.Database.EnsureCreatedAsync();
    for (var index = 0; index < 65; index++) db.Leads.Add(New("ws_a", "member_a", "Visible " + index));
    db.Leads.Add(New("ws_b", "member_a", "Foreign"));
    db.Leads.Add(New("ws_a", "member_b", "Other owner"));
    db.Leads.Add(New("ws_a", null, "Queue"));
    var archived = New("ws_a", "member_a", "Archived"); archived.Archive(null, now); db.Leads.Add(archived);
    var contacting = New("ws_a", "member_a", "Contacting");
    contacting.Advance(LeadWorkState.Contacting, new(null, null, null), now); db.Leads.Add(contacting);
    var nurture = New("ws_a", "member_a", "Nurture");
    nurture.Advance(LeadWorkState.Contacting, new(null, null, null), now);
    nurture.Advance(LeadWorkState.Verifying, new(null, null, null), now);
    Assert(nurture.QualifyForNurture("contact_a", now), "seed canonical nurture"); db.Leads.Add(nurture);
    var disqualified = New("ws_a", "member_a", "Disqualified");
    disqualified.Disqualify("No fit", null, "member_a", now); db.Leads.Add(disqualified);
    await db.SaveChangesAsync(); db.ChangeTracker.Clear();

    var first = await handler.HandleAsync(Q(), default);
    Assert(first.IsSuccess && first.Value!.Items.Count == 50 && first.Value.TotalCount == 65 && first.Value.HasNextPage, "bounded OWN column and authorized count");
    var cursor = first.Value!.NextCursor!;
    var second = await handler.HandleAsync(Q(cursor), default);
    Assert(second.IsSuccess && second.Value!.Items.Count == 15 && second.Value.TotalCount == 65 && !second.Value.HasNextPage, "second SQL keyset window");
    Assert(!first.Value.Items.Select(x => x.Id).Intersect(second.Value!.Items.Select(x => x.Id)).Any(), "equal-time id tie-breaker has no overlap");
    Assert(first.Value.Items.All(x => x.Phone is null), "withheld phone is absent from projection");
    Assert((await handler.HandleAsync(Q(search: "0901234567"), default)).Value!.TotalCount == 0, "withheld phone cannot affect search count");
    evaluator.Workspace = "ws_b";
    Assert((await handler.HandleAsync(Q(cursor), default)).Error?.Code == "VALIDATION_FAILED", "cross-workspace cursor rejected");
    evaluator.Workspace = "ws_a";
    Assert((await handler.HandleAsync(Q(cursor, column: "CONTACTING"), default)).Error?.Code == "VALIDATION_FAILED", "cross-column cursor rejected");
    Assert((await handler.HandleAsync(Q(cursor, search: "Visible"), default)).Error?.Code == "VALIDATION_FAILED", "changed-filter cursor rejected");
    Assert((await handler.HandleAsync(Q(cursor[..^5] + "abcde"), default)).Error?.Code == "VALIDATION_FAILED", "tampered cursor rejected");
    Assert((await handler.HandleAsync(Q(limit: 101), default)).Error?.Code == "VALIDATION_FAILED", "maximum bound enforced");
    Assert((await handler.HandleAsync(Q(column: "invalid"), default)).Error?.Code == "VALIDATION_FAILED", "unknown column rejected");
    Assert((await handler.HandleAsync(Q(column: "CONTACTING"), default)).Value!.TotalCount == 1, "independent contacting count");
    Assert((await handler.HandleAsync(Q(column: "NURTURE"), default)).Value!.TotalCount == 1, "canonical nurture column");
    Assert((await handler.HandleAsync(Q(column: "POSITIVE_OUTCOME"), default)).Value!.TotalCount == 1, "positive column preserves nurture semantics");
    evaluator.HideOutcome = true;
    Assert((await handler.HandleAsync(Q(column: "NURTURE"), default)).Error?.Code == "ACCESS_DENIED", "hidden outcome cannot leak through column count");
    evaluator.HideOutcome = false; evaluator.Allowed = false;
    Assert((await handler.HandleAsync(Q(), default)).Error?.Code == "ACCESS_DENIED", "capability denial enforced");
    evaluator.Allowed = true; evaluator.Scope = RecordAccessScopeFilter.Denied;
    Assert((await handler.HandleAsync(Q(), default)).Value!.TotalCount == 0, "denied scope returns empty");
    evaluator.Scope = RecordAccessScopeFilter.Workspace;
    Assert((await handler.HandleAsync(Q(), default)).Value!.TotalCount == 66, "WORKSPACE scope still excludes foreign workspace and unassigned");
    evaluator.Queue = true;
    Assert((await handler.HandleAsync(Q(), default)).Value!.TotalCount == 67, "explicit queue grant respected");
    Console.WriteLine("Lead Kanban SQL/handler verification completed.");
}
finally { await db.Database.EnsureDeletedAsync(); }

Lead New(string workspace, string? owner, string name) => new(workspace,
    JsonSerializer.Deserialize<LeadProfile>(JsonSerializer.Serialize(new {
        DisplayName = name, OwnerId = owner, Phone = "0901234567", Source = "Test",
        CompanyName = "Company", PainPoint = "Need", NextFollowUpAt = now,
        InterestedProducts = Array.Empty<object>(), Tags = Array.Empty<string>(), CustomFields = Array.Empty<object>()
    }))!, now);
Query Q(string? cursor = null, int? limit = 50, string column = "NEW", string? search = null) => new(cursor, limit, search, column, null, null, null, "req_kanban", "corr_kanban");
static void Assert(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }

// Explicit authorization decisions isolate owner enforcement from AccessControl policy tests.
sealed class Evaluator : IRecordAccessEvaluator
{
    public string Workspace = "ws_a";
    public bool Allowed = true;
    public bool HideOutcome;
    public bool Queue;
    public RecordAccessScopeFilter Scope = RecordAccessScopeFilter.OwnedByMember;
    public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resourceKey, string requiredCapability,
        IReadOnlyList<string>? requestedFields, RecordAccessRepresentation representation,
        RecordAccessRequestContext context, CancellationToken token)
    {
        var fields = (requestedFields ?? []).ToDictionary(x => x, x => x == "phone" || (HideOutcome && x == "qualificationOutcome")
            ? RecordFieldEnforcement.Withheld : RecordFieldEnforcement.ReadWrite);
        var constructor = typeof(RecordAccessAuthorization).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return Task.FromResult((RecordAccessAuthorization)constructor.Invoke([
            Allowed, Allowed ? "AUTHORIZED" : "ACCESS_DENIED", new TrustedWorkspaceContext(Workspace, "account_a", "member_a", "membership_a"),
            Scope, Scope == RecordAccessScopeFilter.OwnedByMember ? "member_a" : null, Scope.ToString(), fields,
            Array.Empty<string>(), new[] { "leads.read" }, "policy_test", resourceKey, requiredCapability, true, true, Queue
        ]));
    }
    public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization authorization, string recordId,
        RecordAccessFacts facts, string enforcementPoint, RecordAccessRequestContext context, CancellationToken token) => throw new NotSupportedException();
}
