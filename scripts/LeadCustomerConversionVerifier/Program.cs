using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Workflows;
using UnicoreCRM.Workflows.Atomic.Contracts;
using LeadParticipant = UnicoreCRM.Crm.Leads.Contracts.ILeadCustomerConversionParticipant;

if(args.Length!=1)throw new ArgumentException("Pass one isolated SQL Server connection string.");
var connection=args[0];var clock=new MutableTimeProvider(DateTimeOffset.Parse("2026-09-12T12:00:00Z"));
var participants=new Participants();var services=new ServiceCollection();
services.AddLogging();services.AddSingleton<TimeProvider>(clock);services.AddSingleton(participants);
services.AddSingleton<LeadParticipant>(participants);
services.AddSingleton<IContactCustomerSubjectParticipant>(participants);
services.AddSingleton<IOrganizationCustomerSubjectParticipant>(participants);
services.AddSingleton<UnicoreCRM.Crm.Customers.Contracts.ILeadCustomerConversionParticipant>(participants);
services.AddSingleton<IServiceAccessAuthorizer>(participants);
services.AddWorkflowsModule(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:UnicoreCRM",connection}}).Build());
var recoveryRunnerType=services.Select(x=>x.ServiceType).Single(x=>x.Name=="ILeadCustomerConversionRecoveryRunner");
await using var provider=services.BuildServiceProvider();

await ClearAnchors();
VerifyOwnerDomainSemantics();
await VerifyCustomerSemantics();
await VerifySameKeyOverlap();await VerifySameIntentOverlap();await VerifyConflictingIntentOverlap();await VerifyFingerprintSeparation();
await VerifyRecoveryDenied();
await VerifyCrashRecovery(CrashPoint.Customer);await VerifyCrashRecovery(CrashPoint.Lead);await VerifyCrashRecovery(CrashPoint.Finalize);
Console.WriteLine("Lead customer conversion concurrency/recovery verifier PASS");

void VerifyOwnerDomainSemantics()
{
    var assembly=typeof(LeadDocument).Assembly;var leadType=assembly.GetType("UnicoreCRM.Crm.Leads.Domain.Lead",true)!;
    var stateType=assembly.GetType("UnicoreCRM.Crm.Leads.Domain.LeadWorkState",true)!;var outcomeType=assembly.GetType("UnicoreCRM.Crm.Leads.Domain.LeadQualificationOutcome",true)!;
    var record=leadType.GetMethod("RecordCustomerConversion",BindingFlags.Instance|BindingFlags.NonPublic)!;
    object Lead(string state,string? outcome,string? customer=null,bool archived=false)
    {var value=RuntimeHelpers.GetUninitializedObject(leadType);Set(value,"WorkState",Enum.Parse(stateType,state));Set(value,"QualificationOutcome",outcome is null?null:Enum.Parse(outcomeType,outcome));Set(value,"CustomerRef",customer);Set(value,"ArchivedAt",archived?clock.GetUtcNow():null);Set(value,"UpdatedAt",clock.GetUtcNow());return value;}
    foreach(var state in new[]{"New","Contacting","Verifying"}){var lead=Lead(state,null);Assert(record.Invoke(lead,["customer",clock.GetUtcNow()])!.ToString()!="Ineligible",$"{state} Lead converts without Deal");Assert(Get(lead,"WorkState")!.ToString()=="Closed"&&Get(lead,"QualificationOutcome")!.ToString()=="Customer",$"{state} closes with CUSTOMER outcome");}
    var nurture=Lead("Closed","Nurture");Set(nurture,"RelationshipType","CONTACT");Set(nurture,"RelationshipId","contact_history");record.Invoke(nurture,["customer",clock.GetUtcNow()]);Assert(Get(nurture,"QualificationOutcome")!.ToString()=="Nurture"&&Get(nurture,"RelationshipId")!.ToString()=="contact_history","NURTURE history is preserved");
    var opportunity=Lead("Closed","Opportunity");Set(opportunity,"DealRef","deal_history");record.Invoke(opportunity,["customer",clock.GetUtcNow()]);Assert(Get(opportunity,"QualificationOutcome")!.ToString()=="Opportunity"&&Get(opportunity,"DealRef")!.ToString()=="deal_history","OPPORTUNITY and DealRef history are preserved");
    Assert(record.Invoke(Lead("Closed","Disqualified"),["customer",clock.GetUtcNow()])!.ToString()=="Ineligible","DISQUALIFIED Lead is rejected");
    Assert(record.Invoke(Lead("New",null,archived:true),["customer",clock.GetUtcNow()])!.ToString()=="Ineligible","archived Lead is rejected");
    Assert(record.Invoke(Lead("Closed","Customer","same"),["same",clock.GetUtcNow()])!.ToString()=="Replayed","same CustomerRef replays");
    Assert(record.Invoke(Lead("Closed","Customer","other"),["same",clock.GetUtcNow()])!.ToString()=="ConflictingCustomer","different CustomerRef conflicts");
    var profile=assembly.GetType("UnicoreCRM.Crm.Customers.Domain.CustomerProfile",true)!;
    foreach(var removed in new[]{"SourceLeadId","ConversionCompletedAt","ConversionInitiatedBy","ConversionResult"})Assert(profile.GetProperty(removed) is null,$"Customer profile omits singular {removed}");
    var provenance=assembly.GetType("UnicoreCRM.Crm.Customers.Domain.CustomerLeadConversionProvenance",true)!;
    foreach(var required in new[]{"WorkspaceId","WorkflowId","SourceLeadId","CustomerId","PolicyVersion","CorrelationId","OriginalPrincipalId","InitiatedAt","Resolution","CompletedAt","CompletionExecutorPrincipalId","CompletionEventId","CompletionAuditId"})Assert(provenance.GetProperty(required,BindingFlags.Instance|BindingFlags.NonPublic) is not null,$"provenance retains {required}");
    var policy=typeof(AccessRequirement).Assembly.GetType("UnicoreCRM.Platform.AccessControl.Application.Common.WorkspaceCapabilityPolicy",true)!;
    var human=(IReadOnlyList<string>)policy.GetProperty("WorkspaceOwnerCapabilities",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
    Assert(human.Contains("leads.convert_to_customer"),"interactive conversion is human-role provisionable");
    Assert(!human.Contains("leads.convert_to_customer.recover"),"recovery capability is not human-role assignable");
    static void Set(object target,string property,object? value)=>target.GetType().GetField($"<{property}>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(target,value);
    static object? Get(object target,string property)=>target.GetType().GetProperty(property,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.GetValue(target);
}

async Task VerifySameKeyOverlap()
{
    participants.Reset();var lead="lead_same_key";participants.BlockCustomer=true;
    var first=Execute(lead,"key-a",1,"contact_a");await participants.CustomerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var second=await Execute(lead,"key-a",1,"contact_a");Assert(!second.IsSuccess&&second.ErrorCode=="LEAD_CONVERSION_IN_PROGRESS","same-key loser is in progress");
    Assert(participants.CustomerCalls==1,"same-key loser did not invoke participant");participants.ReleaseCustomer.TrySetResult();Assert((await first).IsSuccess,"same-key winner completes");
    Assert(await WinnerCount(lead)==1,"same-key has one anchor");
}
async Task VerifyCustomerSemantics()
{
    participants.Reset();var b2c=await Execute("lead_b2c","key-b2c",1,"contact_b2c");Assert(b2c.IsSuccess&&b2c.Response!.Result.CustomerResolution=="CREATED","EXISTING Contact creates B2C Customer");Assert(participants.LastLeadOwner=="lead-owner","created Customer inherits frozen Lead owner");
    var b2b=await Execute("lead_b2b","key-b2b",1,"organization_b2b","ORGANIZATION_ACCOUNT");Assert(b2b.IsSuccess&&b2b.Response!.Result.OrganizationId=="organization_b2b","EXISTING Organization creates B2B Customer");
    var reused=await Execute("lead_b2c_reuse","key-b2c-reuse",1,"contact_b2c");Assert(reused.IsSuccess&&reused.Response!.Result.CustomerResolution=="REUSED"&&reused.Response!.Result.CustomerId==b2c.Response!.Result.CustomerId,"two Leads reuse one exact-subject Customer");Assert(participants.ProvenanceWorkflows.Count==3,"each conversion owns distinct provenance");
}
async Task VerifySameIntentOverlap()
{
    participants.Reset();var lead="lead_same_intent";participants.BlockCustomer=true;
    var first=Execute(lead,"key-b1",5,"contact_b");await participants.CustomerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var second=await Execute(lead,"key-b2",6,"contact_b");Assert(!second.IsSuccess&&second.ErrorCode=="LEAD_CONVERSION_IN_PROGRESS","different-key same-intent joins winner");
    Assert(participants.CustomerCalls==1,"same-intent loser did not invoke participant");participants.ReleaseCustomer.TrySetResult();Assert((await first).IsSuccess,"same-intent winner completes");
    Assert(await WinnerCount(lead)==1,"same intent has one business winner");
}
async Task VerifyConflictingIntentOverlap()
{
    participants.Reset();var lead="lead_conflict";participants.BlockCustomer=true;
    var first=Execute(lead,"key-c1",1,"contact_c1");await participants.CustomerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var second=await Execute(lead,"key-c2",1,"contact_c2");Assert(!second.IsSuccess&&second.ErrorCode=="LEAD_ALREADY_CONVERTED","different subject conflicts");
    Assert(participants.CustomerCalls==1,"conflicting loser did not invoke participant");participants.ReleaseCustomer.TrySetResult();Assert((await first).IsSuccess,"conflicting winner completes");
}
async Task VerifyFingerprintSeparation()
{
    participants.Reset();var lead="lead_fingerprint";Assert((await Execute(lead,"key-d",7,"contact_d")).IsSuccess,"fingerprint fixture completes");
    var changedVersion=await Execute(lead,"key-d",8,"contact_d");Assert(changedVersion.ErrorCode=="IDEMPOTENCY_KEY_REUSED","same key changed version rejected");
    var changedSubject=await Execute(lead,"key-d",7,"contact_other");Assert(changedSubject.ErrorCode=="IDEMPOTENCY_KEY_REUSED","same key changed subject rejected");
    var converged=await Execute(lead,"key-d2",8,"contact_d");Assert(converged.IsSuccess,"different key/version same business intent converges");
}
async Task VerifyCrashRecovery(CrashPoint point)
{
    participants.Reset();participants.Crash=point;var lead=$"lead_crash_{point}";
    try{await Execute(lead,$"key-{point}",1,$"contact_{point}");throw new Exception($"{point} crash was not injected");}catch(InjectedCrashException){}
    clock.Advance(TimeSpan.FromMinutes(3));await using var scope=provider.CreateAsyncScope();
    var runner=scope.ServiceProvider.GetRequiredService(recoveryRunnerType);
    var resumed=(Task<int>)recoveryRunnerType.GetMethod("ResumeDueAsync")!.Invoke(runner,["svc_lead_customer_conversion_recovery",CancellationToken.None])!;
    Assert(await resumed==1,$"{point} crash recovers through service-authorized scanner after lease expiry");Assert(participants.DuplicateEffects==1,$"{point} recovery replays exactly one stable participant effect");
}
async Task VerifyRecoveryDenied()
{
    participants.Reset();participants.Crash=CrashPoint.Customer;var lead="lead_recovery_denied";
    try{await Execute(lead,"key-denied",1,"contact_denied");}catch(InjectedCrashException){}
    clock.Advance(TimeSpan.FromMinutes(3));participants.ServiceAllowed=false;await using var scope=provider.CreateAsyncScope();
    var runner=scope.ServiceProvider.GetRequiredService(recoveryRunnerType);var task=(Task<int>)recoveryRunnerType.GetMethod("ResumeDueAsync")!.Invoke(runner,["svc_lead_customer_conversion_recovery",CancellationToken.None])!;
    Assert(await task==0,"missing Workspace service grant denies recovery");Assert(participants.CustomerCalls==1,"denied recovery mutates no owner state");
    participants.ServiceAllowed=true;var retry=(Task<int>)recoveryRunnerType.GetMethod("ResumeDueAsync")!.Invoke(runner,["svc_lead_customer_conversion_recovery",CancellationToken.None])!;Assert(await retry==1,"Workspace grant allows recovery");
}
async Task<ConvertLeadToCustomerResult> Execute(string lead,string key,long version,string subject,string type="CONTACT")
{ await using var scope=provider.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<ILeadCustomerConversionWorkflow>().ExecuteAsync(Command(lead,key,version,subject,type),CancellationToken.None); }
static ConvertLeadToCustomerCommand Command(string lead,string key,long version,string subject,string type="CONTACT")=>new(lead,new(new(type,"EXISTING",subject)),"request","correlation",key,version);
async Task ClearAnchors(){await using var sql=new SqlConnection(connection);await sql.OpenAsync();await using var command=new SqlCommand("DELETE FROM workflow.LeadCustomerConversionAnchors",sql);await command.ExecuteNonQueryAsync();}
async Task<int> WinnerCount(string lead){await using var sql=new SqlConnection(connection);await sql.OpenAsync();await using var command=new SqlCommand("SELECT COUNT(*) FROM workflow.LeadCustomerConversionAnchors WHERE LeadId=@lead",sql);command.Parameters.AddWithValue("@lead",lead);return Convert.ToInt32(await command.ExecuteScalarAsync());}
static void Assert(bool value,string claim){if(!value)throw new InvalidOperationException("FAIL: "+claim);Console.WriteLine("PASS: "+claim);}

enum CrashPoint{None,Customer,Lead,Finalize}
sealed class InjectedCrashException:Exception;
sealed class MutableTimeProvider(DateTimeOffset now):TimeProvider{private DateTimeOffset current=now;public override DateTimeOffset GetUtcNow()=>current;public void Advance(TimeSpan value)=>current+=value;}
sealed class Participants : LeadParticipant,IContactCustomerSubjectParticipant,IOrganizationCustomerSubjectParticipant,
    UnicoreCRM.Crm.Customers.Contracts.ILeadCustomerConversionParticipant,IServiceAccessAuthorizer
{
    readonly HashSet<string> effects=[];readonly Dictionary<string,string> customers=[];public readonly HashSet<string> ProvenanceWorkflows=[];public string? LastLeadOwner;public int CustomerCalls;public int DuplicateEffects;public bool BlockCustomer;public bool ServiceAllowed=true;public CrashPoint Crash;
    public TaskCompletionSource CustomerEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource ReleaseCustomer=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Reset(){effects.Clear();customers.Clear();ProvenanceWorkflows.Clear();LastLeadOwner=null;CustomerCalls=DuplicateEffects=0;BlockCustomer=false;ServiceAllowed=true;Crash=CrashPoint.None;CustomerEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);ReleaseCustomer=new(TaskCreationOptions.RunContinuationsAsynchronously);}
    static readonly TrustedWorkspaceContext Trusted=new("ws_conversion","account","member","membership");
    public Task<LeadCustomerConversionPreparation> AuthorizeAsync(PrepareLeadCustomerConversionCommand c,CancellationToken t)=>Task.FromResult(new LeadCustomerConversionPreparation(true,Trusted,"lead-owner",c.ExpectedVersion,null,null,null,null));
    public Task<LeadCustomerConversionPreparation> PrepareAsync(PrepareLeadCustomerConversionCommand c,CancellationToken t)=>AuthorizeAsync(c,t);
    Task<ContactCustomerSubject?> IContactCustomerSubjectParticipant.ResolveVisibleAsync(TrustedWorkspaceContext t,string id,string r,string c,CancellationToken x)=>Task.FromResult<ContactCustomerSubject?>(new(id,"Contact",null,null,true,3));
    Task<OrganizationCustomerSubject?> IOrganizationCustomerSubjectParticipant.ResolveVisibleAsync(TrustedWorkspaceContext t,string id,string r,string c,CancellationToken x)=>Task.FromResult<OrganizationCustomerSubject?>(new(id,"Organization",null,null,true,3));
    public async Task<ResolveLeadConversionCustomerResult> ResolveOrCreateAsync(ResolveLeadConversionCustomerCommand c,CancellationToken t)
    {CustomerCalls++;CustomerEntered.TrySetResult();if(BlockCustomer)await ReleaseCustomer.Task.WaitAsync(t);Effect(c.ParticipantKey);LastLeadOwner=c.LeadOwnerId;var subject=$"{c.TrustedWorkspace.WorkspaceId}:{c.SubjectType}:{c.SubjectId}";var resolution=customers.TryGetValue(subject,out var customer)?"REUSED":"CREATED";customer??="customer_"+c.SubjectId;customers[subject]=customer;ProvenanceWorkflows.Add(c.WorkflowId);if(Crash==CrashPoint.Customer){Crash=CrashPoint.None;throw new InjectedCrashException();}return new(true,customer,0,resolution,resolution=="CREATED"?["customer-created"]:[],resolution=="CREATED"?["customer-audit"]:[],null,null);}
    public Task<LeadCustomerConversionRecord> RecordAsync(RecordLeadCustomerConversionCommand c,CancellationToken t)
    {Effect(c.ParticipantKey);if(Crash==CrashPoint.Lead){Crash=CrashPoint.None;throw new InjectedCrashException();}return Task.FromResult(new LeadCustomerConversionRecord(true,false,2,"lead-command",["lead-recorded"],["lead-audit"],null,null));}
    public Task<ResolveLeadConversionCustomerResult> FinalizeAsync(FinalizeLeadConversionCustomerCommand c,CancellationToken t)
    {Effect(c.ParticipantKey);if(Crash==CrashPoint.Finalize){Crash=CrashPoint.None;throw new InjectedCrashException();}return Task.FromResult(new ResolveLeadConversionCustomerResult(true,c.CustomerId,0,c.Resolution,["completed"],["completion-audit"]));}
    public Task<ServiceAccessAuthorizationDecision> AuthorizeAsync(string w,string p,AccessRequirement r,string c,CancellationToken t)=>Task.FromResult(new ServiceAccessAuthorizationDecision(ServiceAllowed,ServiceAllowed?"AUTHORIZED":"ACCESS_DENIED","decision"));
    void Effect(string key){if(!effects.Add(key))DuplicateEffects++;}
}
