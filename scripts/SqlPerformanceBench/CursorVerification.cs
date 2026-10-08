using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.DataProtection;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Application.ListContacts;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

// Explicit test decisions exercise binding in the real handler/persistence. The HTTP suite
// separately tests trusted identity, policy resolution, record access and field enforcement.
internal static class CursorVerification
{
    internal static async Task<IReadOnlyList<string>> Run(IContactsPersistence persistence,SqlConnection fixture)
    {
        var checks=new List<string>();
        var evaluator=new DecisionFixture();
        var tasks=new TasksFixture(evaluator);
        var clock=new ClockFixture();
        var handler=new Handler(new ContactAuthorization(evaluator),persistence,clock,new EphemeralDataProtectionProvider(),tasks,new ZoneFixture());
        Query Q(string? cursor=null,ContactListFilters? filters=null)=>new(new("perf_cursor_request","perf_cursor_correlation"),filters,cursor,25);
        void Check(bool ok,string name){if(!ok)throw new InvalidOperationException(name);checks.Add(name);}
        async Task Rejected(Query query,string name)=>Check((await handler.HandleAsync(query,default)).Error?.Code=="VALIDATION_FAILED",name);
        var first=await handler.HandleAsync(Q(),default);
        Check(first.IsSuccess && first.Value!.Items.Count==25 && first.Value.PageInfo.NextCursor is not null,"bounded first page issues cursor");
        var cursor=first.Value!.PageInfo.NextCursor!;
        var next=await handler.HandleAsync(Q(cursor),default);
        Check(next.IsSuccess && !first.Value.Items.Select(x=>x.Id).Intersect(next.Value!.Items.Select(x=>x.Id)).Any(),"next page has no overlapping IDs");
        await Rejected(Q(cursor[..^5]+"abcde"),"tampered cursor rejected");
        await Rejected(Q(cursor,new(Source:"web")),"changed filter rejected");
        await Rejected(Q(cursor,new(Sort:"nameAsc")),"changed sort rejected");
        evaluator.Member="member_2";await Rejected(Q(cursor),"changed principal rejected");evaluator.Member="member_1";
        evaluator.Workspace="bench_medium_0";await Rejected(Q(cursor),"changed workspace rejected");evaluator.Workspace="bench_large";
        evaluator.Hidden="workEmail";await Rejected(Q(cursor),"changed readable search fields rejected");evaluator.Hidden=null;
        tasks.Owner="member_1";await Rejected(Q(cursor),"changed Tasks assignee authority rejected");tasks.Owner=null;
        tasks.Available=false;await Rejected(Q(cursor),"changed follow-up availability rejected");tasks.Available=true;
        clock.Now=clock.Initial.AddMinutes(59);
        Check((await handler.HandleAsync(Q(cursor),default)).IsSuccess,"cursor valid before one-hour TTL");
        clock.Now=clock.Initial.AddHours(1);await Rejected(Q(cursor),"cursor rejected at exact expiry");
        clock.Now=clock.Initial.AddHours(2);await Rejected(Q(cursor),"expired cursor rejected");
        // Sequential inter-page writes, confined to the owned synthetic fixture. This
        // checks keyset boundaries, not a snapshot or every possible moving-key update.
        clock.Now=clock.Initial;
        using(var write=new SqlCommand("INSERT contacts.Contacts(ContactId,WorkspaceId,OwnerId,FullName,Status,Version,CreatedAt,UpdatedAt,Profile) VALUES(N'contact_live_insert',N'bench_large',N'member_1',N'Synthetic live insert',N'active',0,'2026-01-01','2030-01-01',N'{}'); DELETE contacts.CustomerRelationships WHERE ContactId=@deleted AND WorkspaceId=N'bench_large'; DELETE contacts.Contacts WHERE ContactId=@deleted AND WorkspaceId=N'bench_large'; UPDATE contacts.Contacts SET UpdatedAt='2031-01-01' WHERE ContactId=@updated AND WorkspaceId=N'bench_large';",fixture))
        {
            write.Parameters.AddWithValue("@deleted",next.Value!.Items[0].Id);
            write.Parameters.AddWithValue("@updated",first.Value.Items[0].Id);
            await write.ExecuteNonQueryAsync();
        }
        var afterWrites=await handler.HandleAsync(Q(cursor),default);
        Check(afterWrites.IsSuccess && afterWrites.Value!.Items.Count==25,"continuation remains bounded after inter-page insert/update/delete");
        Check(!afterWrites.Value!.Items.Any(x=>x.Id=="contact_live_insert"),"insert above cursor boundary excluded from continuation");
        Check(!afterWrites.Value.Items.Any(x=>x.Id==next.Value!.Items[0].Id),"deleted continuation row absent");
        Check(!afterWrites.Value.Items.Any(x=>x.Id==first.Value.Items[0].Id),"updated row above cursor boundary excluded");
        return checks;
    }
    private sealed class ClockFixture:TimeProvider
    {
        internal readonly DateTimeOffset Initial=DateTimeOffset.Parse("2026-10-08T12:00:00Z");
        internal DateTimeOffset Now=DateTimeOffset.Parse("2026-10-08T12:00:00Z");
        public override DateTimeOffset GetUtcNow()=>Now;
    }
    private sealed class ZoneFixture:IWorkspaceTimeZoneReader
    {
        public Task<string?> ReadTimeZoneAsync(string workspaceId,CancellationToken token)=>Task.FromResult<string?>("UTC");
    }
    private sealed class TasksFixture(DecisionFixture evaluator):IContactFollowUpReadAuthorityResolver
    {
        internal string? Owner;
        internal bool Available=true;
        public Task<ContactFollowUpAuthorityResult> ResolveAsync(string requestId,string correlationId,CancellationToken token)=>
            Task.FromResult(new ContactFollowUpAuthorityResult(Available?new ContactFollowUpReadAuthority(evaluator.Workspace,Owner):null,Available?null:"ACCESS_DENIED"));
    }
    private sealed class DecisionFixture:IRecordAccessEvaluator
    {
        internal string Workspace="bench_large",Member="member_1";
        internal string? Hidden;
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resourceKey,string requiredCapability,
            IReadOnlyList<string>? requestedFields,RecordAccessRepresentation representation,RecordAccessRequestContext context,CancellationToken token)
        {
            var fields=(requestedFields??[]).ToDictionary(x=>x,x=>x==Hidden?RecordFieldEnforcement.Withheld:RecordFieldEnforcement.ReadWrite);
            var constructor=typeof(RecordAccessAuthorization).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single();
            return Task.FromResult((RecordAccessAuthorization)constructor.Invoke([
                true,"AUTHORIZED",new TrustedWorkspaceContext(Workspace,"account_fixture",Member,"membership_fixture"),
                RecordAccessScopeFilter.Workspace,null,"Workspace",fields,Array.Empty<string>(),new[]{requiredCapability},"policy_fixture",resourceKey,requiredCapability,true,true,false]));
        }
        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization authorization,string recordId,
            RecordAccessFacts facts,string enforcementPoint,RecordAccessRequestContext context,CancellationToken token)=>
            throw new InvalidOperationException("List cursor must not introduce per-row authorization");
    }
}
