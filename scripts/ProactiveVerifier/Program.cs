using UnicoreCRM.AI.Proactive.Application;
using UnicoreCRM.AI.Proactive.Infrastructure;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.PlatformOperations.AiExecution.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.AI.Gateway;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Application.ProvisionInitialWorkspaceAccess;
using UnicoreCRM.Platform.AccessControl.Application.Common;

var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
var clock = new FixedClock(now);
var store = new MemoryStore();
var reconciler = new CustomerHealthRiskReconciler(store, clock);
var passed = 0;
void Check(string name, object? expected, object? actual) { if (!Equals(expected, actual)) throw new InvalidOperationException($"{name}: expected={expected}, actual={actual}"); passed++; }
ProactiveCustomerHealthFact Fact(string band, string? owner="member_a", string status="ACTIVE") => new("customer_a", owner, status, band, band, $"PURCHASE_RECENCY_{band}", "V1", "1");
async Task Run(ProactiveCustomerHealthFact fact) => await reconciler.ReconcilePageAsync("workspace_a", [fact], "correlation-test", default);

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
store.Active=store.Active with{Status=ProactiveValues.Snoozed,SnoozedUntil=clock.Now.AddDays(1)};await Run(Fact("AT_RISK","member_b"));Check("snooze before due remains hidden",ProactiveValues.Snoozed,store.Active!.Status);
clock.Now=clock.Now.AddDays(2);await Run(Fact("AT_RISK","member_b"));Check("due snooze reopens only after current risk evaluation",ProactiveValues.Open,store.Active!.Status);
var unownedId=store.Active!.ItemId;var unownedCycle=store.Active.RiskCycleKey;
await Run(Fact("AT_RISK", null)); Check("unowned preserves active cycle", unownedId, store.Active!.ItemId); Check("unowned preserves risk cycle key", unownedCycle, store.Active.RiskCycleKey); Check("unowned signal", true, store.Audits.Any(x=>x.Action=="UNOWNED_TRIGGER_SUPPRESSED"));
await Run(Fact("AT_RISK", "member_c"));Check("owner after unowned receives same item",unownedId,store.Active!.ItemId);Check("owner after unowned receives same cycle",unownedCycle,store.Active.RiskCycleKey);Check("owner after unowned transferred","member_c",store.Active.OwnerMemberId);
var dualStore=new MemoryStore();var dualReconciler=new CustomerHealthRiskReconciler(dualStore,clock);
await dualReconciler.ReconcilePageAsync("workspace_a",[Fact("AT_RISK","member_a")],"corr-dual",default);await dualReconciler.ReconcilePageAsync("workspace_a",[Fact("CRITICAL","member_b")],"corr-dual",default);
Check("simultaneous owner change audit",true,dualStore.Audits.Any(x=>x.Action=="ITEM_OWNER_CHANGED"));Check("simultaneous escalation audit",true,dualStore.Audits.Any(x=>x.Action=="ITEM_ESCALATED"));
await Run(Fact("CRITICAL", "member_c")); await Run(Fact("CRITICAL", "member_c", "ARCHIVED")); Check("archive resolves", ProactiveValues.Resolved, store.Items.Last().Status);
var utcNext=ProactiveEvaluationWorker.NextLocalDay(now,"UTC"); Check("UTC next local day", new DateTimeOffset(2026,9,21,0,0,0,TimeSpan.Zero), utcNext);
var saigonNext=ProactiveEvaluationWorker.NextLocalDay(now,"SE Asia Standard Time"); Check("Saigon next local day", new DateTimeOffset(2026,9,20,17,0,0,TimeSpan.Zero), saigonNext);
var newYorkNext=ProactiveEvaluationWorker.NextLocalDay(now,"Eastern Standard Time"); Check("New York next local day", new DateTimeOffset(2026,9,21,4,0,0,TimeSpan.Zero), newYorkNext);
try { ProactiveEvaluationWorker.NextLocalDay(now,""); throw new InvalidOperationException("missing timezone accepted"); } catch(ArgumentException) { passed++; }
var pageStore=new MemoryStore();var pageReconciler=new CustomerHealthRiskReconciler(pageStore,clock);
await pageReconciler.ReconcilePageAsync("workspace_a",[new("customer_x","member_a","ACTIVE","UNKNOWN","UNKNOWN","NONE","V1","1"),new("customer_y","member_a","ACTIVE","WATCH","MEDIUM","WATCH","V1","1")],"corr-page",default);
Check("one active-cycle batch read per Customer page",1,pageStore.BatchReads);
var currentOwner=WorkspaceCapabilityPolicy.WorkspaceOwnerCapabilities;
var preProactive=currentOwner.Where(x=>x is not "ai.proactive.use" and not "ai.proactive.manage").Order(StringComparer.Ordinal).ToArray();
var preAi=preProactive.Where(x=>x is not "ai.configuration.read" and not "ai.configuration.manage").Order(StringComparer.Ordinal).ToArray();
Check("pre-Proactive owner snapshot admitted",true,InitialWorkspaceAccessPolicy.IsKnownPreviousCapabilitySet(preProactive));
Check("actual pre-AI configuration snapshot admitted",true,InitialWorkspaceAccessPolicy.IsKnownPreviousCapabilitySet(preAi));
Check("pre-AI snapshot excludes proactive",false,preAi.Any(x=>x.StartsWith("ai.proactive.",StringComparison.Ordinal)));
Check("arbitrary historical subset rejected",false,InitialWorkspaceAccessPolicy.IsKnownPreviousCapabilitySet(preAi.Skip(1).ToArray()));
Check("unexpected historical capability rejected",false,InitialWorkspaceAccessPolicy.IsKnownPreviousCapabilitySet([..preAi,"customers.unexpected"]));
passed += await SuggestionVerifier.RunAsync(now);
if(args.Length==1) passed += await SqlVerifier.RunAsync(args[0], now);
Console.WriteLine($"PROACTIVE_SCENARIO_CORPUS_PASS cases={passed}");

static class SqlVerifier
{
    internal static async Task<int> RunAsync(string connectionString, DateTimeOffset now)
    {
        var builder=new SqlConnectionStringBuilder(connectionString); var database=builder.InitialCatalog;
        try { RequireIsolatedDatabase("UnicoreCRM"); throw new InvalidOperationException("unsafe verifier database accepted"); } catch(ArgumentException) { }
        RequireIsolatedDatabase(database);
        builder.InitialCatalog="master"; await using(var connection=new SqlConnection(builder.ConnectionString)){await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=$"IF DB_ID(N'{database.Replace("'","''")}') IS NOT NULL BEGIN ALTER DATABASE [{database.Replace("]","]]" )}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database.Replace("]","]]" )}]; END; CREATE DATABASE [{database.Replace("]","]]" )}];";await command.ExecuteNonQueryAsync();}
        var options=new DbContextOptionsBuilder<AiExecutionDbContext>().UseSqlServer(connectionString,x=>x.MigrationsHistoryTable("__EFMigrationsHistory","platform_ai")).Options;
        await using(var migration=new AiExecutionDbContext(options)) await migration.Database.MigrateAsync();
        await using(var context=new AiExecutionDbContext(options))
        {
            var store=new EfProactiveStore(context); var policy=new WorkspaceProactivePolicyState("workspace_sql",true,0,null,now,null,null,"member",now);
            await store.SavePolicyAsync(policy,"policy-sql-0001","fingerprint",new("audit_policy","workspace_sql","member","PROACTIVE_ENABLED",null,null,"{}","corr-policy",now),default);
            if(!await store.TryClaimPolicyAsync("workspace_sql",0,"lease_a",now,now.AddMinutes(5),default))throw new InvalidOperationException("initial lease claim failed");
            if(await store.TryClaimPolicyAsync("workspace_sql",0,"lease_b",now,now.AddMinutes(5),default))throw new InvalidOperationException("concurrent lease claim succeeded");
            if(!await store.RenewPolicyLeaseAsync("workspace_sql","lease_a",now.AddMinutes(1),now.AddMinutes(6),default))throw new InvalidOperationException("lease renewal failed");
            if(await store.TryClaimPolicyAsync("workspace_sql",0,"lease_b",now.AddMinutes(5).AddSeconds(30),now.AddMinutes(10),default))throw new InvalidOperationException("renewed lease was reclaimed before expiry");
            if(!await store.TryClaimPolicyAsync("workspace_sql",0,"lease_b",now.AddMinutes(7),now.AddMinutes(12),default))throw new InvalidOperationException("expired lease was not reclaimable");
            try { await store.CompletePolicyEvaluationAsync("workspace_sql","lease_a",now,now.AddDays(1),default); throw new InvalidOperationException("stale worker completed reclaimed lease"); } catch(InvalidOperationException exception) when(exception.Message.Contains("not owned",StringComparison.Ordinal)) { }
            await store.CompletePolicyEvaluationAsync("workspace_sql","lease_b",now.AddMinutes(7),now.AddDays(1),default);
            await store.SavePolicyAsync(new("workspace_expired",true,0,null,now,null,null,"member",now),"policy-expired-0001","expired-fingerprint",new("audit_expired_policy","workspace_expired","member","PROACTIVE_ENABLED",null,null,"{}","corr-expired",now),default);
            if(!await store.TryClaimPolicyAsync("workspace_expired",0,"lease_expired_a",now,now.AddMinutes(1),default))throw new InvalidOperationException("expiry test lease claim failed");
            try { await store.CompletePolicyEvaluationAsync("workspace_expired","lease_expired_a",now.AddMinutes(2),now.AddDays(1),default); throw new InvalidOperationException("expired lease completed without reclaim"); } catch(InvalidOperationException exception) when(exception.Message.Contains("not owned",StringComparison.Ordinal)) { }
            if(!await store.TryClaimPolicyAsync("workspace_expired",0,"lease_expired_b",now.AddMinutes(2),now.AddMinutes(7),default))throw new InvalidOperationException("expired lease was not reclaimable by worker B");
            await store.CompletePolicyEvaluationAsync("workspace_expired","lease_expired_b",now.AddMinutes(3),now.AddDays(1),default);
        }
        static ProactiveItemState Item(string id,string cycle,DateTimeOffset time)=>new(id,"workspace_sql","member",ProactiveValues.CustomerHealthRisk,ProactiveValues.Customer,"customer_sql",ProactiveValues.High,"RISK","fingerprint",cycle,ProactiveValues.Open,time,time,null,null,null,null,"1",0,time,time);
        async Task<string> CreateAsync(string id,string cycle)
        { await using var db=new AiExecutionDbContext(options);try{await new EfProactiveStore(db).SaveItemAsync(Item(id,cycle,now),new($"audit_{id}","workspace_sql",null,"ITEM_CREATED",id,"customer_sql","{}","corr",now),default);return "created";}catch(ProactiveActiveCycleConflictException){return "conflict";} }
        var outcomes=await Task.WhenAll(CreateAsync("item_a","cycle_a"),CreateAsync("item_b","cycle_b"));
        if(outcomes.Count(x=>x=="created")!=1||outcomes.Count(x=>x=="conflict")!=1)throw new InvalidOperationException("concurrent active cycle did not converge");
        await using(var db=new AiExecutionDbContext(options))
        { var store=new EfProactiveStore(db);var active=await store.ReadActiveCycleAsync("workspace_sql","customer_sql",ProactiveValues.CustomerHealthRisk,default)??throw new InvalidOperationException("active cycle missing");await store.SaveItemAsync(active with{Status=ProactiveValues.Resolved,ResolvedAt=now.AddDays(1),Version=1,UpdatedAt=now.AddDays(1)},new("audit_resolve","workspace_sql",null,"ITEM_RESOLVED",active.ItemId,"customer_sql","{}","corr",now.AddDays(1)),default);await store.SaveItemAsync(Item("item_new_cycle","cycle_new",now.AddDays(2)),new("audit_new_cycle","workspace_sql",null,"ITEM_CREATED","item_new_cycle","customer_sql","{}","corr",now.AddDays(2)),default); }
        await using(var db=new AiExecutionDbContext(options))
        { if(await db.ProactiveItems.CountAsync(x=>x.WorkspaceId=="workspace_sql"&&x.SubjectId=="customer_sql")!=2)throw new InvalidOperationException("later risk cycle was not retained separately");if(await db.ProactiveItems.CountAsync(x=>x.Status!=ProactiveValues.Resolved)!=1)throw new InvalidOperationException("active-cycle uniqueness invalid"); }
        var liveNow=DateTimeOffset.UtcNow;
        await using(var db=new AiExecutionDbContext(options))
        {
            var store=new EfProactiveStore(db);
            await store.SavePolicyAsync(new("workspace_long_a",true,0,null,liveNow.AddSeconds(-1),null,null,"member",liveNow),"policy-long-0001","long-fingerprint-a",
                new("audit_long_policy_a","workspace_long_a","member","PROACTIVE_ENABLED",null,null,"{}","corr-long-a",liveNow),default);
            await store.SavePolicyAsync(new("workspace_long_b",true,0,null,liveNow.AddSeconds(-1),null,null,"member",liveNow),"policy-long-0002","long-fingerprint-b",
                new("audit_long_policy_b","workspace_long_b","member","PROACTIVE_ENABLED",null,null,"{}","corr-long-b",liveNow),default);
        }
        var longServices=new ServiceCollection(); longServices.AddSingleton(TimeProvider.System); longServices.AddLogging();
        longServices.AddDbContext<AiExecutionDbContext>(x=>x.UseSqlServer(connectionString,s=>s.MigrationsHistoryTable("__EFMigrationsHistory","platform_ai")));
        longServices.AddScoped<IProactiveStore,EfProactiveStore>(); longServices.AddScoped<IWorkspaceTimeZoneReader,UtcZoneReader>();
        longServices.AddScoped<IProactiveCustomerHealthReader,FirstWorkspaceSlowCustomerReader>(); longServices.AddScoped<CustomerHealthRiskReconciler>(); longServices.AddScoped<ProactiveWorkspaceEvaluator>();
        DateTimeOffset initialExpiry;
        await using(var provider=longServices.BuildServiceProvider())
        {
            var worker=new ProactiveEvaluationWorker(provider.GetRequiredService<IServiceScopeFactory>(),TimeProvider.System,NullLogger<ProactiveEvaluationWorker>.Instance,TimeSpan.FromSeconds(2),TimeSpan.FromMilliseconds(250));
            var evaluation=worker.RunOnceAsync(default);
            while(true)
            {
                await using var observed=new AiExecutionDbContext(options); var policy=await observed.ProactivePolicies.AsNoTracking().SingleAsync(x=>x.WorkspaceId=="workspace_long_a");
                if(policy.LeaseExpiresAt is not null){initialExpiry=policy.LeaseExpiresAt.Value;break;}
                await Task.Delay(10);
            }
            var wait=initialExpiry.AddMilliseconds(500)-DateTimeOffset.UtcNow;if(wait>TimeSpan.Zero)await Task.Delay(wait);
            await using(var contender=new AiExecutionDbContext(options))
                if(await new EfProactiveStore(contender).TryClaimPolicyAsync("workspace_long_a",0,"lease_contender",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddMinutes(1),default))throw new InvalidOperationException("long evaluation lease was reclaimed after its original expiry");
            await evaluation;
        }
        await using(var db=new AiExecutionDbContext(options))
        {
            var first=await db.ProactivePolicies.AsNoTracking().SingleAsync(x=>x.WorkspaceId=="workspace_long_a");
            var second=await db.ProactivePolicies.AsNoTracking().SingleAsync(x=>x.WorkspaceId=="workspace_long_b");
            if(first.LastEvaluationAt is null||first.LeaseId is not null)throw new InvalidOperationException("renewed long evaluation did not complete authoritatively");
            if(second.LastEvaluationAt is null||second.LeaseId is not null||second.LastEvaluationAt<=initialExpiry)throw new InvalidOperationException("second Workspace did not receive a fresh valid lease after the long first evaluation");
        }
        await using(var db=new AiExecutionDbContext(options))
        {
            var store=new EfProactiveStore(db);
            await store.SavePolicyAsync(new("workspace_missing_zone",true,0,null,now,null,null,"member",now),"policy-zone-0001","zone-fingerprint",
                new("audit_zone_policy","workspace_missing_zone","member","PROACTIVE_ENABLED",null,null,"{}","corr-zone",now),default);
            await store.SavePolicyAsync(new("workspace_invalid_zone",true,0,null,now,null,null,"member",now),"policy-zone-0002","zone-fingerprint-invalid",
                new("audit_zone_policy_invalid","workspace_invalid_zone","member","PROACTIVE_ENABLED",null,null,"{}","corr-zone-invalid",now),default);
        }
        var services=new ServiceCollection(); services.AddSingleton<TimeProvider>(new FixedClock(now)); services.AddLogging();
        services.AddDbContext<AiExecutionDbContext>(x=>x.UseSqlServer(connectionString,s=>s.MigrationsHistoryTable("__EFMigrationsHistory","platform_ai")));
        services.AddScoped<IProactiveStore,EfProactiveStore>(); services.AddScoped<IWorkspaceTimeZoneReader,InvalidAndMissingZoneReader>();
        services.AddScoped<IProactiveCustomerHealthReader,EmptyCustomerReader>(); services.AddScoped<CustomerHealthRiskReconciler>(); services.AddScoped<ProactiveWorkspaceEvaluator>();
        await using(var provider=services.BuildServiceProvider())
        { var worker=new ProactiveEvaluationWorker(provider.GetRequiredService<IServiceScopeFactory>(),provider.GetRequiredService<TimeProvider>(),NullLogger<ProactiveEvaluationWorker>.Instance);await worker.RunOnceAsync(default); }
        await using(var db=new AiExecutionDbContext(options))
        {
            foreach(var workspaceId in new[]{"workspace_missing_zone","workspace_invalid_zone"})
            {
                var failed=await db.ProactiveAudits.AnyAsync(x=>x.WorkspaceId==workspaceId&&x.Action=="EVALUATION_FAILED");
                var policy=await db.ProactivePolicies.SingleAsync(x=>x.WorkspaceId==workspaceId);
                if(!failed||policy.LastEvaluationAt is not null||policy.NextEvaluationAt!=now)throw new InvalidOperationException($"{workspaceId} timezone did not fail safely for retry");
            }
        }
        var apiWorkspace="workspace_api";var apiMember="member_api";var apiClock=new FixedClock(now.AddDays(10));
        static ProactiveItemState ApiItem(string workspace,string id,string customer,string owner,DateTimeOffset time)=>new(id,workspace,owner,ProactiveValues.CustomerHealthRisk,ProactiveValues.Customer,customer,ProactiveValues.High,"PURCHASE_RECENCY_AT_RISK","fingerprint",$"cycle_{id}",ProactiveValues.Open,time,time,null,null,null,null,"1",0,time,time);
        await using(var db=new AiExecutionDbContext(options))
        {
            var store=new EfProactiveStore(db);var index=0;
            foreach(var item in new[]{ApiItem(apiWorkspace,"item_page_one","customer_page_one",apiMember,now.AddMinutes(2)),ApiItem(apiWorkspace,"item_page_two","customer_page_two",apiMember,now.AddMinutes(1)),ApiItem(apiWorkspace,"item_visible","customer_visible",apiMember,now),ApiItem(apiWorkspace,"item_other","customer_other","member_other",now),ApiItem(apiWorkspace,"item_reassigned","customer_reassigned",apiMember,now),ApiItem(apiWorkspace,"item_denied","customer_denied",apiMember,now),ApiItem(apiWorkspace,"item_seen","customer_seen",apiMember,now),ApiItem(apiWorkspace,"item_snooze","customer_snooze",apiMember,now),ApiItem(apiWorkspace,"item_dismiss","customer_dismiss",apiMember,now),ApiItem("workspace_foreign","item_foreign","customer_visible",apiMember,now)})
                await store.SaveItemAsync(item,new($"audit_api_{++index}",item.WorkspaceId,null,"ITEM_CREATED",item.ItemId,item.SubjectId,"{}","corr-api",now),default);
        }
        var current=new FakeCurrentWorkspace(apiWorkspace,apiMember);var allowed=new FakeAccessAuthorizer(apiWorkspace,apiMember,true,true);
        var customerMap=new Dictionary<string,CustomerAttentionProjection>{
            ["customer_visible"]=new("customer_visible","Visible Customer"),
            ["customer_seen"]=new("customer_seen","Seen Customer"),["customer_snooze"]=new("customer_snooze","Snooze Customer"),["customer_dismiss"]=new("customer_dismiss","Dismiss Customer"),
            ["customer_page_one"]=new("customer_page_one","Page One"),["customer_page_two"]=new("customer_page_two","Page Two")};
        var customerReader=new FakeAttentionReader(true,customerMap);
        await using(var db=new AiExecutionDbContext(options))
        {
            var app=new ProactiveAttentionApplication(current,allowed,new EfProactiveStore(db),customerReader,apiClock);
            var list=await app.ListAsync(null,100,"request-api","corr-api",default);if(!list.IsSuccess)throw new InvalidOperationException("own inbox denied");
            if(!list.Value!.Items.Any(x=>x.ItemId=="item_visible")||list.Value.Items.Any(x=>x.ItemId is "item_other" or "item_reassigned" or "item_denied" or "item_foreign"))throw new InvalidOperationException("Attention visibility boundary failed");
            if(customerReader.Calls!=1)throw new InvalidOperationException("Attention Customer enrichment was not batched");
            var pageOne=await app.ListAsync(null,1,"request-api","corr-api",default);if(!pageOne.IsSuccess||pageOne.Value!.NextCursor is null)throw new InvalidOperationException("Attention first page cursor missing");
            var pageTwo=await app.ListAsync(pageOne.Value.NextCursor,1,"request-api","corr-api",default);if(!pageTwo.IsSuccess||pageTwo.Value!.Items.Any(x=>x.ItemId==pageOne.Value.Items.Single().ItemId))throw new InvalidOperationException("Attention cursor round trip failed");
            var malformed=await app.ListAsync(new string('A',513),10,"request-api","corr-api",default);if(malformed.Error?.Code!="AI_REQUEST_INVALID"||malformed.Error.FieldErrors?.ContainsKey("cursor")!=true)throw new InvalidOperationException("oversized cursor was not safely rejected");
            var outOfRange=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{long.MaxValue}\nitem_visible")).TrimEnd('=').Replace('+','-').Replace('/','_');
            var invalidTicks=await app.ListAsync(outOfRange,10,"request-api","corr-api",default);
            if(invalidTicks.Error?.Code!="AI_REQUEST_INVALID"||invalidTicks.Error.FieldErrors?.ContainsKey("cursor")!=true)throw new InvalidOperationException("out-of-range ticks did not return cursor validation error");
            if(typeof(CustomerAttentionProjection).GetProperties().Select(x=>x.Name).Order().SequenceEqual(new[]{"CustomerId","DisplayLabel"})!=true)throw new InvalidOperationException("Customer Attention contract exports excess security facts");
            var badLimit=await app.ListAsync(null,101,"request-api","corr-api",default);if(badLimit.Error?.FieldErrors?.ContainsKey("limit")!=true)throw new InvalidOperationException("invalid limit was not attributed to limit");
            if((await app.DetailAsync("item_other","request-api","corr-api",default)).Error?.Code!="AI_PROACTIVE_ITEM_NOT_FOUND")throw new InvalidOperationException("other owner detail was visible");
            if((await app.DetailAsync("item_reassigned","request-api","corr-api",default)).Error?.Code!="AI_PROACTIVE_ITEM_NOT_FOUND")throw new InvalidOperationException("reassigned Customer remained visible to old owner");
            if((await app.DetailAsync("item_denied","request-api","corr-api",default)).Error?.Code!="AI_PROACTIVE_ITEM_NOT_FOUND")throw new InvalidOperationException("record-scope denied Customer leaked through detail");
            if((await app.DetailAsync("item_foreign","request-api","corr-api",default)).Error?.Code!="AI_PROACTIVE_ITEM_NOT_FOUND")throw new InvalidOperationException("foreign Workspace item was visible");
            var missingUse=new ProactiveAttentionApplication(current,new FakeAccessAuthorizer(apiWorkspace,apiMember,false,true),new EfProactiveStore(db),customerReader,apiClock);
            if((await missingUse.ListAsync(null,10,"request-api","corr-api",default)).Error?.Code!="AI_PROACTIVE_ACCESS_DENIED")throw new InvalidOperationException("missing proactive use was not denied");
            var missingCustomers=new ProactiveAttentionApplication(current,allowed,new EfProactiveStore(db),new FakeAttentionReader(false,customerMap),apiClock);
            if((await missingCustomers.ListAsync(null,10,"request-api","corr-api",default)).Error?.Code!="AI_PROACTIVE_ACCESS_DENIED")throw new InvalidOperationException("missing customers view was not denied");
            var hiddenHealth=new ProactiveAttentionApplication(current,allowed,new EfProactiveStore(db),new FakeAttentionReader(true,new Dictionary<string,CustomerAttentionProjection>()),apiClock);
            if((await hiddenHealth.ListAsync(null,100,"request-api","corr-api",default)).Value?.Items.Count!=0)throw new InvalidOperationException("hidden Customer Health leaked Attention items");
            var seen=await app.SeenAsync("item_seen",0,"seen-key-0001","request-api","corr-api",default);if(!seen.IsSuccess||seen.Value!.Item.Status!=ProactiveValues.Open||seen.Value.Item.SeenAt is null)throw new InvalidOperationException("seen semantics failed");
            var seenReplay=await app.SeenAsync("item_seen",0,"seen-key-0001","request-api","corr-api",default);if(!seenReplay.IsSuccess||seenReplay.Value!.Item.Version!=1)throw new InvalidOperationException("item action replay failed");
            if((await app.SeenAsync("item_seen",0,"seen-key-0002","request-api","corr-api",default)).Error?.Code!="AI_PROACTIVE_VERSION_CONFLICT")throw new InvalidOperationException("stale item action version was not rejected");
            var snooze=await app.SnoozeAsync("item_snooze",apiClock.Now.AddDays(1),0,"snooze-key-0001","request-api","corr-api",default);if(!snooze.IsSuccess||snooze.Value!.Item.Status!=ProactiveValues.Snoozed)throw new InvalidOperationException("snooze semantics failed");
            var afterSnooze=await app.ListAsync(null,100,"request-api","corr-api",default);if(afterSnooze.Value!.Items.Any(x=>x.ItemId=="item_snooze"))throw new InvalidOperationException("snoozed item remained visible before due");
            var dismiss=await app.DismissAsync("item_dismiss",0,"dismiss-key-0001","request-api","corr-api",default);if(!dismiss.IsSuccess||dismiss.Value!.Item.Status!=ProactiveValues.Dismissed)throw new InvalidOperationException("dismiss semantics failed");
            var noManage=new ProactiveAttentionApplication(current,new FakeAccessAuthorizer(apiWorkspace,apiMember,true,false),new EfProactiveStore(db),customerReader,apiClock);
            if((await noManage.SaveConfigurationAsync(true,0,"manage-key-0001","corr-api",default)).Error?.Code!="AI_PROACTIVE_ACCESS_DENIED")throw new InvalidOperationException("missing proactive manage was not denied");
            var initialConfiguration=await app.GetConfigurationAsync("corr-api",default);if(!initialConfiguration.IsSuccess||initialConfiguration.Value!.Enabled||initialConfiguration.Value.Version!=0)throw new InvalidOperationException("default proactive configuration was not disabled");
            var enabled=await app.SaveConfigurationAsync(true,0,"config-key-0001","corr-api",default);if(!enabled.IsSuccess||!enabled.Value!.Enabled||enabled.Value.Version!=1)throw new InvalidOperationException("enable configuration failed");
            var replay=await app.SaveConfigurationAsync(true,0,"config-key-0001","corr-api",default);if(!replay.IsSuccess||replay.Value!.Version!=1)throw new InvalidOperationException("configuration replay failed");
            if((await app.SaveConfigurationAsync(false,0,"config-key-0001","corr-api",default)).Error?.Code!="AI_PROACTIVE_IDEMPOTENCY_KEY_REUSED")throw new InvalidOperationException("changed configuration intent reused idempotency key");
            if((await app.SaveConfigurationAsync(false,0,"config-key-0002","corr-api",default)).Error?.Code!="AI_PROACTIVE_VERSION_CONFLICT")throw new InvalidOperationException("stale configuration version was not rejected");
            var disabled=await app.SaveConfigurationAsync(false,1,"config-key-0003","corr-api",default);if(!disabled.IsSuccess||disabled.Value!.Enabled||disabled.Value.Version!=2)throw new InvalidOperationException("disable configuration failed");
        }
        await using(var db=new AiExecutionDbContext(options))
        {
            if(await db.ProactiveAudits.CountAsync(x=>x.WorkspaceId==apiWorkspace&&x.Action=="PROACTIVE_ENABLED")!=1||await db.ProactiveAudits.CountAsync(x=>x.WorkspaceId==apiWorkspace&&x.Action=="PROACTIVE_DISABLED")!=1)throw new InvalidOperationException("configuration audit evidence missing");
            if(await db.ProactiveAudits.CountAsync(x=>x.WorkspaceId==apiWorkspace&&new[]{"ITEM_SEEN","ITEM_SNOOZED","ITEM_DISMISSED"}.Contains(x.Action))!=3)throw new InvalidOperationException("item action audit evidence missing");
        }
        var concurrentItem=ApiItem("workspace_concurrent_item","item_concurrent","customer_concurrent","member_concurrent",now);
        await using(var db=new AiExecutionDbContext(options))await new EfProactiveStore(db).SaveItemAsync(concurrentItem,new("audit_concurrent_seed","workspace_concurrent_item",null,"ITEM_CREATED",concurrentItem.ItemId,concurrentItem.SubjectId,"{}","corr",now),default);
        var itemBarrier=new CommitBarrier();
        var itemRaceOptions=new DbContextOptionsBuilder<AiExecutionDbContext>().UseSqlServer(connectionString).AddInterceptors(itemBarrier).Options;
        async Task<ProactiveItemCommit> ConcurrentItemAction(string auditId)
        {
            await using var db=new AiExecutionDbContext(itemRaceOptions);var next=concurrentItem with{SeenAt=now.AddMinutes(1),Version=1,UpdatedAt=now.AddMinutes(1)};
            return await new EfProactiveStore(db).CommitItemActionAsync(next,"member_concurrent","seenProactiveItem",0,"same-item-key","same-item-fingerprint",new(auditId,next.WorkspaceId,next.OwnerMemberId,"ITEM_SEEN",next.ItemId,next.SubjectId,"{}","corr",now.AddMinutes(1)),default);
        }
        var itemCommits=await Task.WhenAll(ConcurrentItemAction("audit_concurrent_item_a"),ConcurrentItemAction("audit_concurrent_item_b"));
        if(itemCommits.Count(x=>x.Status==ProactiveCommitStatus.Committed)!=1||itemCommits.Count(x=>x.Status==ProactiveCommitStatus.Replayed)!=1)throw new InvalidOperationException("same-key item action did not converge to committed plus replayed");
        async Task<ProactivePolicyCommit> ConcurrentConfiguration(string workspace,string key,string fingerprint,string auditId)
        {
            await using var db=new AiExecutionDbContext(options);return await new EfProactiveStore(db).SavePolicyConfigurationAsync(workspace,"member_concurrent",true,0,key,fingerprint,new(auditId,workspace,"member_concurrent","PROACTIVE_ENABLED",null,null,"{}","corr",now),now,default);
        }
        var configCommits=await Task.WhenAll(ConcurrentConfiguration("workspace_concurrent_config","same-config-key","same-config-fingerprint","audit_concurrent_config_a"),ConcurrentConfiguration("workspace_concurrent_config","same-config-key","same-config-fingerprint","audit_concurrent_config_b"));
        if(configCommits.Count(x=>x.Status==ProactiveCommitStatus.Committed)!=1||configCommits.Count(x=>x.Status==ProactiveCommitStatus.Replayed)!=1)throw new InvalidOperationException("same-key configuration creation did not converge to committed plus replayed");
        var distinctCommits=await Task.WhenAll(ConcurrentConfiguration("workspace_distinct_config","distinct-key-a","distinct-fingerprint","audit_distinct_config_a"),ConcurrentConfiguration("workspace_distinct_config","distinct-key-b","distinct-fingerprint","audit_distinct_config_b"));
        if(distinctCommits.Count(x=>x.Status==ProactiveCommitStatus.Committed)!=1||distinctCommits.Count(x=>x.Status==ProactiveCommitStatus.VersionConflict)!=1)throw new InvalidOperationException("different-key initial configuration race was not a version conflict");
        await using(var db=new AiExecutionDbContext(options))
        {
            if(await db.ProactiveCommands.CountAsync(x=>x.WorkspaceId=="workspace_concurrent_item")!=1||await db.ProactiveAudits.CountAsync(x=>x.WorkspaceId=="workspace_concurrent_item"&&x.Action=="ITEM_SEEN")!=1)throw new InvalidOperationException("duplicate item command or audit committed");
            if(await db.ProactiveCommands.CountAsync(x=>x.WorkspaceId=="workspace_concurrent_config")!=1||await db.ProactiveAudits.CountAsync(x=>x.WorkspaceId=="workspace_concurrent_config"&&x.Action=="PROACTIVE_ENABLED")!=1)throw new InvalidOperationException("duplicate configuration command or audit committed");
        }
        foreach(var action in new[]{ProactiveValues.Snoozed,ProactiveValues.Dismissed})
        {
            var item=ApiItem("workspace_race_"+action,"item_race_"+action,"customer_race_"+action,"member_race",now);
            await using(var seed=new AiExecutionDbContext(options))await new EfProactiveStore(seed).SaveItemAsync(item,new("seed_"+action,item.WorkspaceId,null,"ITEM_CREATED",item.ItemId,item.SubjectId,"{}","race",now),default);
            // The scheduler's page snapshot is captured before the real user commit.
            await using var schedulerDb=new AiExecutionDbContext(options);var schedulerStore=new EfProactiveStore(schedulerDb);
            var snapshot=(await schedulerStore.ReadActiveCyclesAsync(item.WorkspaceId,[item.SubjectId],item.TriggerType,default))[item.SubjectId];
            var userState=item with{Status=action,Version=1,SeenAt=now,SnoozedUntil=action==ProactiveValues.Snoozed?now.AddDays(1):null,DismissedAt=action==ProactiveValues.Dismissed?now:null};
            await using(var userDb=new AiExecutionDbContext(options))
            {
                var result=await new EfProactiveStore(userDb).CommitItemActionAsync(userState,item.OwnerMemberId,action,0,"race-key","race-intent",new("user_"+action,item.WorkspaceId,item.OwnerMemberId,action,item.ItemId,item.SubjectId,"{}","race",now),default);
                if(result.Status!=ProactiveCommitStatus.Committed)throw new InvalidOperationException("race user action did not commit");
            }
            try
            {
                await schedulerStore.SaveItemWithAuditsAsync(snapshot with{Version=1,LastDetectedAt=now.AddMinutes(1)},[new("stale_"+action,item.WorkspaceId,null,"ITEM_REFRESHED",item.ItemId,item.SubjectId,"{}","race",now)],default);
                throw new InvalidOperationException("stale scheduler overwrote user state");
            }
            catch(DbUpdateConcurrencyException) { }
            await using var verify=new AiExecutionDbContext(options);
            var stored=await new EfProactiveStore(verify).ReadItemAsync(item.WorkspaceId,item.ItemId,default);
            if(stored!.Status!=action||stored.SeenAt!=now||stored.SnoozedUntil!=userState.SnoozedUntil||stored.DismissedAt!=userState.DismissedAt||await verify.ProactiveAudits.AnyAsync(x=>x.AuditId=="stale_"+action))throw new InvalidOperationException("user action or atomic reconciliation audit violated");
            await new CustomerHealthRiskReconciler(new EfProactiveStore(verify),new FixedClock(now)).ReconcilePageAsync(item.WorkspaceId,[new(item.SubjectId,item.OwnerMemberId,"ACTIVE","AT_RISK","HIGH","RISK","V1","1")],"fresh",default);
            if((await new EfProactiveStore(verify).ReadItemAsync(item.WorkspaceId,item.ItemId,default))!.Status!=action)throw new InvalidOperationException("fresh reconciliation lost user state");
        }
        foreach(var raceKind in new[]{"same","changed","different-key"})
        {
            var changedIntent=raceKind=="changed";
            var workspace="workspace_existing_"+raceKind;
            await ConcurrentConfiguration(workspace,"seed","seed","audit_seed_"+raceKind);
            var barrier=new CommitBarrier();
            var raceOptions=new DbContextOptionsBuilder<AiExecutionDbContext>().UseSqlServer(connectionString).AddInterceptors(barrier).Options;
            async Task<ProactivePolicyCommit> UpdatePolicy(bool second)
            {
                await using var db=new AiExecutionDbContext(raceOptions);
                return await new EfProactiveStore(db).SavePolicyConfigurationAsync(workspace,"member_concurrent",!(changedIntent&&second),1,raceKind=="different-key"&&second?"other-key":"update-key",changedIntent&&second?"different":"same",
                    new("audit_update_"+raceKind+second,workspace,"member_concurrent","CONFIG_UPDATED",null,null,"{}","race",now),now,default);
            }
            var commits=await Task.WhenAll(UpdatePolicy(false),UpdatePolicy(true));
            var expected=raceKind=="different-key"?ProactiveCommitStatus.VersionConflict:changedIntent?ProactiveCommitStatus.IdempotencyConflict:ProactiveCommitStatus.Replayed;
            if(commits.Count(x=>x.Status==ProactiveCommitStatus.Committed)!=1||commits.Count(x=>x.Status==expected)!=1)throw new InvalidOperationException("existing policy race failed: "+string.Join(',',commits.Select(x=>x.Status)));
            await using var verify=new AiExecutionDbContext(options);
            if(await verify.ProactiveCommands.CountAsync(x=>x.WorkspaceId==workspace&&x.IdempotencyKey!="seed")!=1||await verify.ProactiveAudits.CountAsync(x=>x.WorkspaceId==workspace&&x.Action=="CONFIG_UPDATED")!=1||(await verify.ProactivePolicies.SingleAsync(x=>x.WorkspaceId==workspace)).Version!=2)throw new InvalidOperationException("existing policy duplicate mutation/audit/command");
        }
        await using(var db=new AiExecutionDbContext(options))
        {
            var store=new EfProactiveStore(db);
            var currentItem=(await store.ReadItemAsync(concurrentItem.WorkspaceId,concurrentItem.ItemId,default))!;
            try
            {
                await store.CommitItemActionAsync(currentItem with{Version=currentItem.Version+1},currentItem.OwnerMemberId,"brokenAudit",currentItem.Version,"broken-key","broken-intent",new("broken_item_audit",currentItem.WorkspaceId,currentItem.OwnerMemberId,new string('X',81),currentItem.ItemId,currentItem.SubjectId,"{}","broken",now),default);
                throw new InvalidOperationException("unexpected item persistence error was disguised as a conflict");
            }
            catch(DbUpdateException) { }
            try
            {
                await store.SavePolicyConfigurationAsync("workspace_existing_same","member_concurrent",false,2,"broken-key","broken-intent",new("broken_policy_audit","workspace_existing_same","member_concurrent",new string('X',81),null,null,"{}","broken",now),now,default);
                throw new InvalidOperationException("unexpected policy persistence error was disguised as a conflict");
            }
            catch(DbUpdateException) { }
            if(await db.ProactiveCommands.AnyAsync(x=>x.IdempotencyKey=="broken-key"))throw new InvalidOperationException("failed audit persisted command");
        }
        await using(var db=new AiExecutionDbContext(options))
        {
            foreach(var workspace in new[]{"workspace_dirty_failure","workspace_after_failure"})
                await new EfProactiveStore(db).SavePolicyAsync(new(workspace,true,0,null,workspace=="workspace_dirty_failure"?now.AddDays(-2):now.AddDays(-1),null,null,"member",now),workspace,workspace,new("seed_"+workspace,workspace,null,"POLICY_CREATED",null,null,"{}","failure",now),default);
        }
        var failureServices=new ServiceCollection();failureServices.AddSingleton<TimeProvider>(new FixedClock(now));failureServices.AddLogging();
        failureServices.AddDbContext<AiExecutionDbContext>(x=>x.UseSqlServer(connectionString));
        failureServices.AddScoped<IProactiveStore,EfProactiveStore>();failureServices.AddScoped<IWorkspaceTimeZoneReader,UtcZoneReader>();
        failureServices.AddScoped<IProactiveCustomerHealthReader,DirtyFailureCustomerReader>();failureServices.AddScoped<CustomerHealthRiskReconciler>();failureServices.AddScoped<ProactiveWorkspaceEvaluator>();
        await using(var provider=failureServices.BuildServiceProvider())
            await new ProactiveEvaluationWorker(provider.GetRequiredService<IServiceScopeFactory>(),provider.GetRequiredService<TimeProvider>(),NullLogger<ProactiveEvaluationWorker>.Instance).RunOnceAsync(default);
        await using(var db=new AiExecutionDbContext(options))
        {
            if(await db.ProactiveAudits.CountAsync(x=>x.WorkspaceId=="workspace_dirty_failure"&&x.Action=="EVALUATION_FAILED")!=1)throw new InvalidOperationException("failure evidence not recorded in clean context");
            if(await db.ProactiveAudits.AnyAsync(x=>x.AuditId=="dirty_uncommitted"))throw new InvalidOperationException("failure audit flushed dirty changes");
            if((await db.ProactivePolicies.SingleAsync(x=>x.WorkspaceId=="workspace_after_failure")).LastEvaluationAt is null)throw new InvalidOperationException("one Workspace failure prevented later Workspace attempt");
        }
        Console.WriteLine("PASS | SQL stale scheduler vs Snooze/Dismiss; fresh retry; existing-policy concurrent replay/conflict; cursor long.MaxValue; clean failure audit and later Workspace");
        return 79 + await SuggestionSqlVerifier.RunAsync(options, now);
    }

    private static void RequireIsolatedDatabase(string database)
    {
        if(string.IsNullOrWhiteSpace(database)||!database.StartsWith("UnicoreCRM_Proactive_Verifier_",StringComparison.Ordinal))
            throw new ArgumentException("Proactive verifier requires an explicitly isolated database name.",nameof(database));
    }
}

sealed class CommitBarrier:Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    private int arrivals;
    private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,CancellationToken cancellationToken=default)
    {
        if(Interlocked.Increment(ref arrivals)==2)ready.TrySetResult();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20),cancellationToken);
        return result;
    }
}

sealed class DirtyFailureCustomerReader(AiExecutionDbContext db):IProactiveCustomerHealthReader
{
    public async Task<ProactiveCustomerHealthPage> ReadPageAsync(string workspaceId,string? cursor,int limit,CancellationToken cancellationToken)
    {
        if(workspaceId=="workspace_dirty_failure")
        {
            db.ProactiveAudits.Add(new ProactiveAuditRow{AuditId="dirty_uncommitted",WorkspaceId=workspaceId,Action=new string('X',81),CorrelationId="dirty",OccurredAt=DateTimeOffset.UtcNow});
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("fixture should fail persistence");
        }
        return new([],null);
    }
}

sealed class InvalidAndMissingZoneReader:IWorkspaceTimeZoneReader { public Task<string?> ReadTimeZoneAsync(string workspaceId,CancellationToken cancellationToken)=>Task.FromResult<string?>(workspaceId.EndsWith("invalid_zone",StringComparison.Ordinal)?"Invalid/Zone":null); }
sealed class UtcZoneReader:IWorkspaceTimeZoneReader { public Task<string?> ReadTimeZoneAsync(string workspaceId,CancellationToken cancellationToken)=>Task.FromResult<string?>("UTC"); }
sealed class EmptyCustomerReader:IProactiveCustomerHealthReader { public Task<ProactiveCustomerHealthPage> ReadPageAsync(string workspaceId,string? cursor,int limit,CancellationToken cancellationToken)=>Task.FromResult(new ProactiveCustomerHealthPage([],null)); }
sealed class FirstWorkspaceSlowCustomerReader:IProactiveCustomerHealthReader { public async Task<ProactiveCustomerHealthPage> ReadPageAsync(string workspaceId,string? cursor,int limit,CancellationToken cancellationToken){if(workspaceId=="workspace_long_a")await Task.Delay(3000,cancellationToken);return new([],null);} }
sealed class FakeCurrentWorkspace(string workspaceId,string memberId):ICurrentWorkspace { private readonly TrustedWorkspaceContext value=new(workspaceId,"account",memberId,"membership");public bool IsResolved=>true;public TrustedWorkspaceContext Require()=>value; }
sealed class FakeAccessAuthorizer(string workspaceId,string memberId,bool use,bool manage):IAccessAuthorizer
{
    public Task<AccessAuthorizationDecision> AuthorizeAsync(AccessRequirement requirement,string correlationId,CancellationToken cancellationToken)
    { var allowed=requirement.Capability switch{"ai.proactive.use"=>use,"ai.proactive.manage"=>manage,_=>false};var context=allowed?new AuthorizationContextDocument(workspaceId,"membership",memberId,"account",[],[],[requirement.Capability],[],[],[],DateTimeOffset.UtcNow):null;return Task.FromResult(new AccessAuthorizationDecision(allowed,allowed?"AUTHORIZED":"ACCESS_DENIED",context)); }
}
sealed class FakeAttentionReader(bool authorized,IReadOnlyDictionary<string,CustomerAttentionProjection> source):ICustomerAttentionReader
{
    public int Calls{get;private set;}
    public Task<CustomerAttentionReadResult> ReadAuthorizedAsync(IReadOnlyCollection<string> ids,CustomerAttentionRequestContext context,CancellationToken cancellationToken){Calls++;return Task.FromResult(new CustomerAttentionReadResult(authorized,source.Where(x=>ids.Contains(x.Key)).ToDictionary()));}
}

sealed class FixedClock(DateTimeOffset now):TimeProvider { public DateTimeOffset Now {get;set;}=now; public override DateTimeOffset GetUtcNow()=>Now; }
sealed class MemoryStore:IProactiveStore
{
    public int BatchReads {get;private set;}
    public List<ProactiveItemState> Items {get;}=[]; public List<ProactiveAuditEvidence> Audits {get;}=[];
    public ProactiveItemState? Active { get=>Items.LastOrDefault(x=>x.Status!=ProactiveValues.Resolved); set { if(value is null)return; var i=Items.FindIndex(x=>x.ItemId==value.ItemId); if(i<0)Items.Add(value);else Items[i]=value; } }
    public Task<ProactiveItemState?> ReadActiveCycleAsync(string w,string s,string t,CancellationToken c)=>Task.FromResult(Active);
    public Task<IReadOnlyDictionary<string,ProactiveItemState>> ReadActiveCyclesAsync(string w,IReadOnlyCollection<string> ids,string t,CancellationToken c){BatchReads++;return Task.FromResult<IReadOnlyDictionary<string,ProactiveItemState>>(Active is null?new Dictionary<string,ProactiveItemState>():new Dictionary<string,ProactiveItemState>{{Active.SubjectId,Active}});}
    public Task SaveItemAsync(ProactiveItemState i,ProactiveAuditEvidence a,CancellationToken c){Active=i;Audits.Add(a);return Task.CompletedTask;}
    public Task SaveItemWithAuditsAsync(ProactiveItemState i,IReadOnlyCollection<ProactiveAuditEvidence> a,CancellationToken c){Active=i;Audits.AddRange(a);return Task.CompletedTask;}
    public Task RecordAuditAsync(ProactiveAuditEvidence a,CancellationToken c){Audits.Add(a);return Task.CompletedTask;}
    public Task<WorkspaceProactivePolicyState?> ReadPolicyAsync(string w,CancellationToken c)=>Task.FromResult<WorkspaceProactivePolicyState?>(null);
    public Task<IReadOnlyList<WorkspaceProactivePolicyState>> ReadDuePoliciesAsync(DateTimeOffset n,int l,CancellationToken c)=>Task.FromResult<IReadOnlyList<WorkspaceProactivePolicyState>>([]);
    public Task<bool> TryClaimPolicyAsync(string w,long v,string l,DateTimeOffset n,DateTimeOffset u,CancellationToken c)=>Task.FromResult(false);
    public Task<bool> RenewPolicyLeaseAsync(string w,string l,DateTimeOffset n,DateTimeOffset u,CancellationToken c)=>Task.FromResult(false);
    public Task CompletePolicyEvaluationAsync(string w,string l,DateTimeOffset e,DateTimeOffset n,CancellationToken c)=>Task.CompletedTask;
    public Task<IReadOnlyList<ProactiveItemState>> ReadOwnerItemsAsync(string w,string o,string s,int l,CancellationToken c)=>Task.FromResult<IReadOnlyList<ProactiveItemState>>([]);
    public Task<IReadOnlyList<ProactiveItemState>> ReadOwnerOpenItemsAsync(string w,string o,DateTimeOffset? b,string? i,int l,CancellationToken c)=>Task.FromResult<IReadOnlyList<ProactiveItemState>>(Items.Where(x=>x.WorkspaceId==w&&x.OwnerMemberId==o&&x.Status==ProactiveValues.Open).Take(l).ToArray());
    public Task<ProactiveItemState?> ReadItemAsync(string w,string i,CancellationToken c)=>Task.FromResult(Items.SingleOrDefault(x=>x.ItemId==i));
    public Task SavePolicyAsync(WorkspaceProactivePolicyState p,string i,string f,ProactiveAuditEvidence a,CancellationToken c)=>Task.CompletedTask;
    public Task<ProactiveItemCommit> CommitItemActionAsync(ProactiveItemState i,string m,string o,long v,string k,string f,ProactiveAuditEvidence a,CancellationToken c){Active=i;Audits.Add(a);return Task.FromResult(new ProactiveItemCommit(ProactiveCommitStatus.Committed,i));}
    public Task<ProactivePolicyCommit> SavePolicyConfigurationAsync(string w,string m,bool e,long v,string k,string f,ProactiveAuditEvidence a,DateTimeOffset n,CancellationToken c)=>Task.FromResult(new ProactivePolicyCommit(ProactiveCommitStatus.Committed,new(w,e,v+1,null,e?n:null,null,null,m,n)));
}
