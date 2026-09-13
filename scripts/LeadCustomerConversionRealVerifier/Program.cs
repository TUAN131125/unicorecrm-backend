using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Crm.Customers.Infrastructure.Persistence;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Crm.Leads.Infrastructure.Persistence;
using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;
using UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;
using UnicoreCRM.Workflows.Atomic.Domain;
using UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;

if(args.Length!=1)throw new ArgumentException("Pass one isolated SQL Server connection string.");
var connection=args[0];var now=DateTimeOffset.Parse("2026-09-12T16:30:00Z");
var customerOptions=new DbContextOptionsBuilder<CustomersDbContext>().UseSqlServer(connection).Options;
var accessOptions=new DbContextOptionsBuilder<AccessControlDbContext>().UseSqlServer(connection).Options;
var leadOptions=new DbContextOptionsBuilder<LeadsDbContext>().UseSqlServer(connection).Options;
var workflowOptions=new DbContextOptionsBuilder<WorkflowsDbContext>().UseSqlServer(connection).Options;

await using(var db=new CustomersDbContext(customerOptions)){
 var provenance=new CustomerLeadConversionProvenance("ws_persistence","conversion_new","customer_1","lead_1","v2","corr-provenance","human_1","CREATED",now);
 db.LeadConversionProvenance.Add(provenance);await db.SaveChangesAsync();db.ChangeTracker.Clear();
 var loaded=await db.LeadConversionProvenance.SingleAsync(x=>x.WorkflowId=="conversion_new");Assert(loaded.InitiatedAt==now,"new provenance InitiatedAt survives EF/SQL reload");
 await db.Database.ExecuteSqlRawAsync("INSERT INTO [customers].[LeadConversionProvenance] ([WorkspaceId],[WorkflowId],[CustomerId],[SourceLeadId],[PolicyVersion],[CorrelationId],[OriginalPrincipalId],[InitiatedAt],[Resolution]) VALUES ('ws_persistence','conversion_legacy','customer_legacy','lead_legacy','v1','corr-legacy','human_legacy',NULL,'REUSED')");
 db.ChangeTracker.Clear();var legacy=await db.LeadConversionProvenance.SingleAsync(x=>x.WorkflowId=="conversion_legacy");Assert(legacy.InitiatedAt is null,"legacy provenance preserves LEGACY_UNKNOWN null");
}

await using(var db=new AccessControlDbContext(accessOptions)){db.WorkspaceServiceCapabilityGrants.Add(new("ws_a","svc_test","leads.convert_to_customer.recover",now));await db.SaveChangesAsync();}
await Authorize("ws_a","corr-allow",true);await Authorize("ws_b","corr-deny-wrong-workspace",false);await Authorize("ws_a","corr-deny-no-grant",false,"svc_without_grant");
await using(var db=new AccessControlDbContext(accessOptions)){
 var decisions=await db.ServiceAuthorizationDecisions.AsNoTracking().ToListAsync();
 Assert(decisions.Single(x=>x.CorrelationId=="corr-allow").Allowed==true,"ALLOW evidence persists true after fresh DbContext");
 Assert(decisions.Single(x=>x.CorrelationId=="corr-deny-wrong-workspace").Allowed==false,"wrong-Workspace DENY evidence persists false");
 Assert(decisions.Single(x=>x.CorrelationId=="corr-deny-no-grant").Allowed==false,"no-grant DENY evidence persists false");
 Assert(decisions.All(x=>x.ServicePrincipalId.Length>0&&x.RequiredCapability=="leads.convert_to_customer.recover"),"service principal and capability persist");
}

foreach(var mutation in new[]{"DISQUALIFY","ARCHIVE","NURTURE","OPPORTUNITY"})await ReservationBlocks(mutation);
await InverseLifecycleWins("DISQUALIFY");await InverseLifecycleWins("ARCHIVE");await ReservationConsumptionAndRelease();
await RealOwnerRecovery();
Console.WriteLine("Lead customer conversion real persistence/reservation verifier PASS");

async Task Authorize(string workspace,string correlation,bool expected,string principal="svc_test"){
 await using var db=new AccessControlDbContext(accessOptions);var service=new ServiceAccessAuthorizer(db,new FixedClock(now));
 var result=await service.AuthorizeAsync(workspace,principal,AccessRequirement.ForCanonicalCapability("leads.convert_to_customer.recover"),correlation,default);
 Assert(result.IsAllowed==expected,$"actual ServiceAccessAuthorizer returns {(expected?"ALLOW":"DENY")} for {correlation}");
}
async Task ReservationBlocks(string mutation){
 var lead=NewLead($"owner_{mutation}");await using(var seed=new LeadsDbContext(leadOptions)){seed.Leads.Add(lead);await seed.SaveChangesAsync();}
 var committed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
 var reservationTask=Task.Run(async()=>{await using var reserve=new LeadsDbContext(leadOptions);var stored=await reserve.Leads.SingleAsync(x=>x.LeadId==lead.LeadId);Assert(stored.ReserveCustomerConversion($"conversion_{mutation}",now)==LeadCustomerConversionReservationResult.Reserved,$"{mutation}: reservation commits");await reserve.SaveChangesAsync();committed.SetResult();});
 var lifecycleTask=Task.Run(async()=>{await committed.Task;await using var competing=new LeadsDbContext(leadOptions);var stored=await competing.Leads.SingleAsync(x=>x.LeadId==lead.LeadId);var blocked=mutation switch{"DISQUALIFY"=>!stored.Disqualify("reason",null,"actor",now),"ARCHIVE"=>!stored.Archive("reason",now),"NURTURE"=>!stored.QualifyForNurture("contact",now),_=>!stored.QualifyForOpportunity("contact","deal",now)};Assert(blocked,$"{mutation}: overlapping lifecycle mutation blocked by committed reservation");});
 await Task.WhenAll(reservationTask,lifecycleTask);
}
async Task InverseLifecycleWins(string mutation){
 var lead=NewLead($"owner_inverse_{mutation}");if(mutation=="DISQUALIFY")Assert(lead.Disqualify("reason",null,"actor",now),"inverse Disqualify commits first");else Assert(lead.Archive("reason",now),"inverse Archive commits first");
 var result=lead.ReserveCustomerConversion($"conversion_inverse_{mutation}",now);Assert(result==LeadCustomerConversionReservationResult.Ineligible,$"inverse {mutation}: reservation fails before Customer side effects");
}
async Task ReservationConsumptionAndRelease(){
 var consumed=NewLead("owner_consumed");Assert(consumed.ReserveCustomerConversion("conversion_consumed",now)==LeadCustomerConversionReservationResult.Reserved,"successful flow reserves Lead");Assert(consumed.RecordCustomerConversion("conversion_consumed",true,"customer_consumed",now)==LeadCustomerConversionRecordResult.Recorded,"final record consumes matching reservation");Assert(consumed.PendingCustomerConversionId is null&&consumed.CustomerRef=="customer_consumed","successful final conversion clears reservation");
 var terminal=NewLead("owner_terminal");terminal.ReserveCustomerConversion("conversion_terminal",now);Assert(terminal.ReleaseCustomerConversion("conversion_terminal",now),"deterministic terminal failure releases matching reservation");Assert(terminal.PendingCustomerConversionId is null,"terminal release clears reservation");
 var transient=NewLead("owner_transient");transient.ReserveCustomerConversion("conversion_transient",now);Assert(transient.PendingCustomerConversionId=="conversion_transient","transient failure retains reservation for recovery");await Task.CompletedTask;
}
async Task RealOwnerRecovery(){
 const string workspace="ws_race",original="human_initiator",servicePrincipal="svc_lead_customer_conversion_recovery";var trusted=new TrustedWorkspaceContext(workspace,"account",original,"membership");var lead=NewLead("owner_A");
 await using(var seed=new LeadsDbContext(leadOptions)){seed.Leads.Add(lead);await seed.SaveChangesAsync();}
 var anchor=new LeadCustomerConversionAnchor("scope_owner_replay",workspace,lead.LeadId,"idem_owner_replay","request_fp","business_fp",0,"account",original,"membership",original,"corr-recovery","request","CONTACT","contact_recovery",1,"owner_A",now);
 await using(var workflowDb=new WorkflowsDbContext(workflowOptions)){workflowDb.LeadCustomerConversionAnchors.Add(anchor);await workflowDb.SaveChangesAsync();}
 var conversion=anchor.ConversionId;
 await using(var leadDb=new LeadsDbContext(leadOptions)){var handler=new UnicoreCRM.Crm.Leads.Application.RecordCustomerConversion.Handler(null!,new EfLeadsPersistence(leadDb),new FixedClock(now));var reserved=await handler.ReserveAsync(new(trusted,lead.LeadId,conversion,$"{conversion}:lead-reserve",0,"request","corr-recovery",original,original),default);Assert(reserved.IsSuccess&&!reserved.Replayed,"real Leads participant commits reservation while owner_A is authoritative");}
 int reservationAudits,reservationEvents;
 await using(var evidenceDb=new LeadsDbContext(leadOptions)){reservationAudits=await evidenceDb.AuditRecords.CountAsync(x=>x.AggregateId==lead.LeadId&&x.Operation=="reserveLeadCustomerConversion");reservationEvents=await evidenceDb.OutboxMessages.CountAsync(x=>x.AggregateId==lead.LeadId&&x.EventType=="LEAD_CUSTOMER_CONVERSION_RESERVED");Assert(reservationAudits==1&&reservationEvents==1,"initial reservation writes exactly one audit and event");}
 await using(var mutationDb=new LeadsDbContext(leadOptions)){var mutable=await mutationDb.Leads.SingleAsync(x=>x.LeadId==lead.LeadId);mutable.ReplaceProfile(mutable.Profile with{OwnerId="owner_B"},now.AddSeconds(1));await mutationDb.SaveChangesAsync();Assert(mutable.Profile.OwnerId=="owner_B","allowed profile mutation changes Lead owner after reservation commit");}
 await using(var grantDb=new AccessControlDbContext(accessOptions)){grantDb.WorkspaceServiceCapabilityGrants.Add(new(workspace,servicePrincipal,"leads.convert_to_customer.recover",now));await grantDb.SaveChangesAsync();}
 string decisionId;await using(var authorizationDb=new AccessControlDbContext(accessOptions)){var decision=await new ServiceAccessAuthorizer(authorizationDb,new FixedClock(now)).AuthorizeAsync(workspace,servicePrincipal,AccessRequirement.ForCanonicalCapability("leads.convert_to_customer.recover"),"corr-recovery",default);Assert(decision.IsAllowed,"real recovery evaluates actual AccessControl grant");decisionId=decision.DecisionId;}
 var recoveryNow=now.AddMinutes(2);LeadCustomerConversionAnchor recovered;
 await using(var workflowDb=new WorkflowsDbContext(workflowOptions)){recovered=await workflowDb.LeadCustomerConversionAnchors.SingleAsync(x=>x.ScopeKey=="scope_owner_replay");Assert(recovered.Stage==LeadCustomerConversionStage.SubjectResolved&&recovered.FrozenLeadOwnerId=="owner_A","crash leaves protocol-v2 anchor before LeadReserved with owner_A frozen");recovered.AcquireLease("recovery_attempt",servicePrincipal,recoveryNow,TimeSpan.FromMinutes(1));await workflowDb.SaveChangesAsync();
  await using(var leadDb=new LeadsDbContext(leadOptions)){var handler=new UnicoreCRM.Crm.Leads.Application.RecordCustomerConversion.Handler(null!,new EfLeadsPersistence(leadDb),new FixedClock(recoveryNow));var replay=await handler.ReserveAsync(new(trusted,lead.LeadId,conversion,$"{conversion}:lead-reserve",0,"request","corr-recovery",original,servicePrincipal),default);Assert(replay.IsSuccess&&replay.Replayed,"recovery idempotently replays the stable Lead reservation key");recovered.RecordLeadReservation("recovery_attempt",replay.LeadVersion!.Value,replay.EmittedEventIds,replay.AuditEvidenceIds,recoveryNow);}
  await workflowDb.SaveChangesAsync();Assert(recovered.FrozenLeadOwnerId=="owner_A","reservation replay checkpoint preserves the admitted frozen owner");}
 await using(var replayEvidenceDb=new LeadsDbContext(leadOptions)){Assert(await replayEvidenceDb.AuditRecords.CountAsync(x=>x.AggregateId==lead.LeadId&&x.Operation=="reserveLeadCustomerConversion")==reservationAudits,"reservation replay creates no duplicate audit");Assert(await replayEvidenceDb.OutboxMessages.CountAsync(x=>x.AggregateId==lead.LeadId&&x.EventType=="LEAD_CUSTOMER_CONVERSION_RESERVED")==reservationEvents,"reservation replay creates no duplicate event");}
 ResolveLeadConversionCustomerResult first;
 await using(var customerDb=new CustomersDbContext(customerOptions)){var participant=new UnicoreCRM.Crm.Customers.Application.ResolveForLeadConversion.Participant(new EfCustomersPersistence(customerDb),new FixedClock(recoveryNow));first=await participant.ResolveOrCreateAsync(new(trusted,"CONTACT","contact_recovery",lead.LeadId,conversion,$"{conversion}:customer-resolve","request","corr-recovery",original,recovered.FrozenLeadOwnerId,servicePrincipal),default);Assert(first.IsSuccess&&first.Resolution=="CREATED","recovery creates Customer after reservation replay");}
 await using(var leadDb=new LeadsDbContext(leadOptions)){var handler=new UnicoreCRM.Crm.Leads.Application.RecordCustomerConversion.Handler(null!,new EfLeadsPersistence(leadDb),new FixedClock(now));var recorded=await handler.RecordAsync(new(trusted,lead.LeadId,first.CustomerId!,conversion,2,$"{conversion}:lead-record","request","corr-recovery",original,servicePrincipal),default);Assert(recorded.IsSuccess,"real recovery records CustomerRef through actual Leads participant");}
 await using(var customerDb=new CustomersDbContext(customerOptions)){var participant=new UnicoreCRM.Crm.Customers.Application.ResolveForLeadConversion.Participant(new EfCustomersPersistence(customerDb),new FixedClock(now));var final=await participant.FinalizeAsync(new(trusted,first.CustomerId!,lead.LeadId,conversion,$"{conversion}:customer-provenance-finalize","request","corr-recovery",original,servicePrincipal,"CREATED"),default);Assert(final.IsSuccess,"real recovery finalizes provenance through actual Customers participant");}
 await using(var verifyCustomers=new CustomersDbContext(customerOptions)){var provenance=await verifyCustomers.LeadConversionProvenance.AsNoTracking().SingleAsync(x=>x.WorkflowId==conversion);Assert(provenance.OriginalPrincipalId==original&&provenance.CompletionExecutorPrincipalId==servicePrincipal,"recovery preserves OriginalPrincipal and persists service CompletionExecutor");var created=await verifyCustomers.Customers.SingleAsync(x=>x.WorkspaceId==workspace&&x.RelationshipId=="contact_recovery");Assert(created.OwnerId=="owner_A","CREATED Customer retains owner_A frozen before reservation");Assert(await verifyCustomers.Customers.CountAsync(x=>x.WorkspaceId==workspace&&x.RelationshipId=="contact_recovery")==1,"recovery creates exactly one Customer");Assert(await verifyCustomers.OutboxMessages.CountAsync(x=>x.WorkspaceId==workspace&&x.EventType=="LEAD_CUSTOMER_CONVERSION_COMPLETED")==1,"recovery creates no duplicate provenance completion event");}
 await using(var verifyLead=new LeadsDbContext(leadOptions)){Assert((await verifyLead.Leads.SingleAsync(x=>x.LeadId==lead.LeadId)).Profile.OwnerId=="owner_B","Lead retains otherwise-valid owner_B profile mutation");}
 await using(var verifyAccess=new AccessControlDbContext(accessOptions)){Assert(await verifyAccess.ServiceAuthorizationDecisions.AnyAsync(x=>x.DecisionId==decisionId&&x.Allowed==true),"recovery authorization decision id and ALLOW outcome persist");}
}
static Lead NewLead(string owner)=>new("ws_race",new("Lead",null,null,null,"0901",null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,null,"Direct",null,owner,null,null,null,[],null,null,null,null,null,null,[],null,null,[]),DateTimeOffset.Parse("2026-09-12T16:00:00Z"));
static void Assert(bool value,string message){if(!value)throw new InvalidOperationException("FAIL: "+message);Console.WriteLine("PASS: "+message);}
sealed class FixedClock(DateTimeOffset now):TimeProvider{public override DateTimeOffset GetUtcNow()=>now;}
