using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Application.ListLeads;
using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

var protection = new EphemeralDataProtectionProvider();
var evaluator = new Evaluator();
var persistence = DispatchProxy.Create<ILeadsPersistence, RecordingPersistence>();
var recording = (RecordingPersistence)persistence;
var handler = new Handler(new LeadAuthorization(evaluator), persistence, TimeProvider.System, protection);
var checks = 0;
var first = await handler.HandleAsync(Q(), default);
Assert(first.IsSuccess && first.Value!.NextCursor is not null, "first page emits continuation");
var cursor = first.Value!.NextCursor!;
await Accept(Q(cursor), "same context continuation");
await Reject(Q(cursor), () => evaluator.Workspace = "ws_b", "foreign workspace"); evaluator.Workspace = "ws_a";
await Reject(Q(cursor, search: "other"), null, "search change");
await Reject(Q(cursor, state: "NEW"), null, "work state change");
await Reject(Q(cursor, owner: "member_a"), null, "owner change");
await Reject(Q(cursor, assignment: "ASSIGNED"), null, "assignment change");
await Reject(Q(cursor), () => evaluator.Scope = RecordAccessScopeFilter.OwnedByMember, "effective OWN scope"); evaluator.Scope = RecordAccessScopeFilter.Workspace;
evaluator.Scope = RecordAccessScopeFilter.OwnedByMember;
var own = await handler.HandleAsync(Q(), default);
await Reject(Q(own.Value!.NextCursor), () => evaluator.Member = "member_b", "OWN scope owner change"); evaluator.Member = "member_a";
evaluator.Scope = RecordAccessScopeFilter.Workspace;
await Reject(Q(cursor), () => evaluator.Phone = false, "phone readability change"); evaluator.Phone = true;
await Reject(Q(cursor), () => evaluator.Queue = false, "unassigned authority change"); evaluator.Queue = true;
await Reject(Q(cursor), () => evaluator.Fingerprint = "policy_changed", "effective policy change"); evaluator.Fingerprint = "policy_test";
evaluator.Member = "member_b"; await Accept(Q(cursor), "equivalent principal authority"); evaluator.Member = "member_a";
await Accept(Q(cursor, limit: 25), "page size change");
var searched = await handler.HandleAsync(Q(search: "  Visible  "), default);
await Accept(Q(searched.Value!.NextCursor, search: "visible"), "canonical search continuation");
await Reject(Q(searched.Value!.NextCursor, search: "OTHER"), null, "different non-null searches");
var newState = await handler.HandleAsync(Q(state: "NEW"), default);
await Reject(Q(newState.Value!.NextCursor, state: "CONTACTING"), null, "NEW to CONTACTING context");
var ownedFilter = await handler.HandleAsync(Q(owner: "member_a"), default);
await Reject(Q(ownedFilter.Value!.NextCursor, owner: "member_b"), null, "different owner filters");
var assigned = await handler.HandleAsync(Q(assignment: "ASSIGNED"), default);
await Reject(Q(assigned.Value!.NextCursor, assignment: "UNASSIGNED"), null, "ASSIGNED to UNASSIGNED context");
await Reject(Q(own.Value!.NextCursor), null, "OWN to WORKSPACE context");
await Accept(Q(cursor, search: "  "), "empty normalized search");
foreach (var value in new[] { "", "%%%", cursor[..20], new string('a', 4097), cursor[..12] + (cursor[12] == 'A' ? 'B' : 'A') + cursor[13..], LeadListCursor.Encode(recording.Leads[0]) })
    await Reject(Q(value), null, "malformed/tampered/legacy token");
var codec = new LeadTableCursor(protection, "fixture");
foreach (var value in new[] { protection.CreateProtector("Leads.KanbanCursor.v1", "fixture").Protect(LeadListCursor.Encode(recording.Leads[0])), protection.CreateProtector("Leads.ListCursor.v1", "fixture").Protect("invalid"), protection.CreateProtector("Leads.ListCursor.v1", "fixture").Protect(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("9999999999999999999\nlead_a"))) })
    Assert(!codec.TryParse(value, new Dictionary<string, string[]>(), out _, out _), "wrong purpose or authenticated invalid keyset rejected");
evaluator.Allowed = false;
var before = recording.Queries;
var denied = await handler.HandleAsync(Q(cursor), default);
Assert(denied.Error?.Code == "ACCESS_DENIED" && recording.Queries == before, "capability denial precedes persistence");
evaluator.Allowed = true; evaluator.Scope = RecordAccessScopeFilter.Denied;
before = recording.Queries;
var empty = await handler.HandleAsync(Q(), default);
Assert(empty.IsSuccess && empty.Value!.TotalCount == 0 && recording.Queries == before, "denied record scope returns empty without persistence");
await Reject(Q(cursor), null, "changed denied scope rejects prior token");
Console.WriteLine($"Completed {checks} focused assertions; no database used.");

Query Q(string? token = null, int limit = 1, string? search = null, string? state = null, string? owner = null, string? assignment = null) => new(token, limit, search, state, owner, assignment, "req_cursor", "corr_cursor");
void Assert(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks++; Console.WriteLine("PASS " + name); }
async Task Accept(Query query, string name) => Assert((await handler.HandleAsync(query, default)).IsSuccess, name);
async Task Reject(Query query, Action? change, string name)
{
    change?.Invoke(); var before = recording.Queries;
    var result = await handler.HandleAsync(query, default);
    Assert(result.Error?.Code == "VALIDATION_FAILED" && recording.Queries == before, name + " rejected before List/Count");
}

public class RecordingPersistence : DispatchProxy
{
    public int Queries;
    internal IReadOnlyList<Lead> Leads { get; } = Enumerable.Range(0, 2).Select(i => new Lead("ws_a", JsonSerializer.Deserialize<LeadProfile>(JsonSerializer.Serialize(new { DisplayName = "Visible " + i, OwnerId = "member_a", Phone = "0901234567", InterestedProducts = Array.Empty<object>(), Tags = Array.Empty<string>(), CustomFields = Array.Empty<object>() }))!, DateTimeOffset.Parse("2026-10-11T00:00:00Z"))).ToArray();
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
    {
        "ListLeadsAsync" => List(),
        "CountLeadsAsync" => Count(),
        "AddAudit" => null,
        "SaveChangesAsync" => Task.CompletedTask,
        _ => throw new NotSupportedException(method.Name)
    };
    private Task<IReadOnlyList<Lead>> List() { Queries++; return Task.FromResult(Leads); }
    private Task<long> Count() { Queries++; return Task.FromResult(2L); }
}

sealed class Evaluator : IRecordAccessEvaluator
{
    public string Workspace = "ws_a", Member = "member_a", Fingerprint = "policy_test";
    public bool Allowed = true, Phone = true, Queue = true;
    public RecordAccessScopeFilter Scope = RecordAccessScopeFilter.Workspace;
    public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resourceKey, string requiredCapability, IReadOnlyList<string>? requestedFields, RecordAccessRepresentation representation, RecordAccessRequestContext context, CancellationToken token)
    {
        var fields = (requestedFields ?? []).ToDictionary(x => x, x => x == "phone" && !Phone ? RecordFieldEnforcement.Withheld : RecordFieldEnforcement.ReadWrite);
        var ctor = typeof(RecordAccessAuthorization).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return Task.FromResult((RecordAccessAuthorization)ctor.Invoke([Allowed, Allowed ? "AUTHORIZED" : "ACCESS_DENIED", new TrustedWorkspaceContext(Workspace, "account_a", Member, "membership_a"), Scope, Scope == RecordAccessScopeFilter.OwnedByMember ? Member : null, Scope.ToString(), fields, Array.Empty<string>(), new[] { "leads.read" }, Fingerprint, resourceKey, requiredCapability, true, true, Queue]));
    }
    public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization authorization, string recordId, RecordAccessFacts facts, string enforcementPoint, RecordAccessRequestContext context, CancellationToken token) => throw new NotSupportedException();
}
