using UnicoreCRM.AI.Proactive.Application;
using UnicoreCRM.AI.Proactive.Infrastructure;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
var clock = new FixedClock(now);
var store = new MemoryStore();
var reconciler = new CustomerHealthRiskReconciler(store, clock);
var passed = 0;
void Check(string name, object? expected, object? actual) { if (!Equals(expected, actual)) throw new InvalidOperationException($"{name}: expected={expected}, actual={actual}"); passed++; }
ProactiveCustomerHealthFact Fact(string band, string? owner="member_a", string status="ACTIVE") => new("customer_a", owner, status, band, band, $"PURCHASE_RECENCY_{band}", "V1", "1");
async Task Run(ProactiveCustomerHealthFact fact) => await reconciler.ReconcileAsync("workspace_a", fact, "correlation-test", default);

await Run(Fact("UNKNOWN")); Check("unknown no item", 0, store.Items.Count);
await Run(Fact("HEALTHY")); Check("healthy no item", 0, store.Items.Count);
await Run(Fact("WATCH")); Check("watch no item", 0, store.Items.Count);
await Run(Fact("AT_RISK")); Check("at risk high", ProactiveValues.High, store.Active!.Severity);
var firstId=store.Active.ItemId; await Run(Fact("AT_RISK")); Check("stable dedup same item", firstId, store.Active!.ItemId); Check("stable one row", 1, store.Items.Count);
store.Active = store.Active with { Status=ProactiveValues.Dismissed, DismissedAt=now }; await Run(Fact("AT_RISK")); Check("dismiss stable remains", ProactiveValues.Dismissed, store.Active!.Status);
await Run(Fact("CRITICAL")); Check("escalation same item", firstId, store.Active!.ItemId); Check("escalation critical", ProactiveValues.Critical, store.Active.Severity); Check("escalation reopens", ProactiveValues.Open, store.Active.Status);
store.Active = store.Active with { Status=ProactiveValues.Snoozed, SnoozedUntil=now.AddDays(3) }; await Run(Fact("AT_RISK", "member_b")); Check("owner transfer", "member_b", store.Active!.OwnerMemberId); Check("owner transfer reopens", ProactiveValues.Open, store.Active.Status); Check("owner transfer clears snooze", null, store.Active.SnoozedUntil);
await Run(Fact("WATCH", "member_b")); Check("recovery resolves", ProactiveValues.Resolved, store.Items.Single(x=>x.ItemId==firstId).Status);
clock.Now=now.AddDays(5); await Run(Fact("AT_RISK", "member_b")); Check("later risk new cycle", false, store.Active!.ItemId==firstId); Check("history retained", 2, store.Items.Count);
await Run(Fact("AT_RISK", null)); Check("unowned resolves active", ProactiveValues.Resolved, store.Items.Last().Status); Check("unowned signal", true, store.Audits.Any(x=>x.Action=="UNOWNED_TRIGGER_SUPPRESSED"));
await Run(Fact("CRITICAL", "member_b")); await Run(Fact("CRITICAL", "member_b", "ARCHIVED")); Check("archive resolves", ProactiveValues.Resolved, store.Items.Last().Status);
var utcNext=ProactiveEvaluationWorker.NextLocalDay(now,"UTC"); Check("UTC next local day", new DateTimeOffset(2026,9,21,0,0,0,TimeSpan.Zero), utcNext);
Console.WriteLine($"PROACTIVE_SCENARIO_CORPUS_PASS cases={passed}");

sealed class FixedClock(DateTimeOffset now):TimeProvider { public DateTimeOffset Now {get;set;}=now; public override DateTimeOffset GetUtcNow()=>Now; }
sealed class MemoryStore:IProactiveStore
{
    public List<ProactiveItemState> Items {get;}=[]; public List<ProactiveAuditEvidence> Audits {get;}=[];
    public ProactiveItemState? Active { get=>Items.LastOrDefault(x=>x.Status!=ProactiveValues.Resolved); set { if(value is null)return; var i=Items.FindIndex(x=>x.ItemId==value.ItemId); if(i<0)Items.Add(value);else Items[i]=value; } }
    public Task<ProactiveItemState?> ReadActiveCycleAsync(string w,string s,string t,CancellationToken c)=>Task.FromResult(Active);
    public Task SaveItemAsync(ProactiveItemState i,ProactiveAuditEvidence a,CancellationToken c){Active=i;Audits.Add(a);return Task.CompletedTask;}
    public Task RecordAuditAsync(ProactiveAuditEvidence a,CancellationToken c){Audits.Add(a);return Task.CompletedTask;}
    public Task<WorkspaceProactivePolicyState?> ReadPolicyAsync(string w,CancellationToken c)=>Task.FromResult<WorkspaceProactivePolicyState?>(null);
    public Task<IReadOnlyList<WorkspaceProactivePolicyState>> ReadDuePoliciesAsync(DateTimeOffset n,int l,CancellationToken c)=>Task.FromResult<IReadOnlyList<WorkspaceProactivePolicyState>>([]);
    public Task<bool> TryClaimPolicyAsync(string w,long v,string l,DateTimeOffset n,DateTimeOffset u,CancellationToken c)=>Task.FromResult(false);
    public Task CompletePolicyEvaluationAsync(string w,string l,DateTimeOffset e,DateTimeOffset n,CancellationToken c)=>Task.CompletedTask;
    public Task<IReadOnlyList<ProactiveItemState>> ReadOwnerItemsAsync(string w,string o,string s,int l,CancellationToken c)=>Task.FromResult<IReadOnlyList<ProactiveItemState>>([]);
    public Task<ProactiveItemState?> ReadItemAsync(string w,string i,CancellationToken c)=>Task.FromResult(Items.SingleOrDefault(x=>x.ItemId==i));
    public Task SavePolicyAsync(WorkspaceProactivePolicyState p,string i,string f,ProactiveAuditEvidence a,CancellationToken c)=>Task.CompletedTask;
}
