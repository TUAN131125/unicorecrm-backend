using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Workflows.Atomic.Contracts;
using UnicoreCRM.Workflows.Atomic.Domain;
using UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;
using CustomerParticipant = UnicoreCRM.Crm.Customers.Contracts.ILeadCustomerConversionParticipant;
using LeadParticipant = UnicoreCRM.Crm.Leads.Contracts.ILeadCustomerConversionParticipant;

namespace UnicoreCRM.Workflows.Atomic.Application.ConvertLeadToCustomer;

internal interface ILeadCustomerConversionRecoveryRunner { Task<int> ResumeDueAsync(string executorId, CancellationToken cancellationToken); }
internal enum LeadCustomerConversionFaultPoint { AfterCustomerCommit, AfterLeadCommit, AfterProvenanceCommit }
internal interface ILeadCustomerConversionFaultInjector { Task AfterParticipantCommitAsync(LeadCustomerConversionFaultPoint point, CancellationToken cancellationToken); }
internal sealed class NoopLeadCustomerConversionFaultInjector : ILeadCustomerConversionFaultInjector
{ public Task AfterParticipantCommitAsync(LeadCustomerConversionFaultPoint point, CancellationToken cancellationToken) => Task.CompletedTask; }

internal sealed class Handler(WorkflowsDbContext db, LeadParticipant leads, IContactCustomerSubjectParticipant contactSubjects,
    IOrganizationCustomerSubjectParticipant organizationSubjects, CustomerParticipant customers,
    IServiceAccessAuthorizer serviceAccess, ILeadCustomerConversionFaultInjector faults,
    TimeProvider timeProvider, ILogger<Handler> logger) : ILeadCustomerConversionWorkflow, ILeadCustomerConversionRecoveryRunner
{
    internal const string RecoveryPrincipal = "svc_lead_customer_conversion_recovery";
    private static readonly AccessRequirement RecoveryRequirement = AccessRequirement.ForCanonicalCapability("leads.convert_to_customer.recover");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private const string Operation = "convertLeadToCustomer";

    public async Task<ConvertLeadToCustomerResult> ExecuteAsync(ConvertLeadToCustomerCommand command, CancellationToken cancellationToken)
    {
        var validation=Validate(command.Request); if(validation is not null)return Failure("VALIDATION_FAILED",422,validation);
        var authorization=await leads.AuthorizeAsync(new(command.LeadId,command.RequestId,command.CorrelationId,command.ExpectedVersion),cancellationToken);
        if(!authorization.IsSuccess)return Failure(MapLeadCode(authorization.ErrorCode),authorization.ErrorStatus??403,current:authorization.CurrentVersion,expected:command.ExpectedVersion);
        var trusted=authorization.TrustedWorkspace!;var subject=command.Request.AccountSubject!;
        var normalizedType=subject.Type!.Trim().ToUpperInvariant();var normalizedId=subject.Id!.Trim();
        var requestFingerprint=Hash(JsonSerializer.Serialize(new{command.LeadId,command.ExpectedVersion,SubjectType=normalizedType,SubjectMode="EXISTING",SubjectId=normalizedId},Json));
        var businessFingerprint=Hash(JsonSerializer.Serialize(new{trusted.WorkspaceId,command.LeadId,ConversionType="LEAD_TO_CUSTOMER",SubjectType=normalizedType,SubjectId=normalizedId},Json));
        var scope=Hash($"{trusted.WorkspaceId}\n{Operation}\n{command.LeadId}\n{command.IdempotencyKey}");
        var existing=await db.LeadCustomerConversionAnchors.AsNoTracking().SingleOrDefaultAsync(x=>x.ScopeKey==scope,cancellationToken);
        if(existing is not null)
        {
            if(existing.RequestFingerprint!=requestFingerprint)return Failure("IDEMPOTENCY_KEY_REUSED",409,idempotency:command.IdempotencyKey);
            return await ResumeAsync(existing.ScopeKey,trusted.MemberId,$"execution_{Guid.NewGuid():N}",cancellationToken,true);
        }
        var winner=await db.LeadCustomerConversionAnchors.AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==trusted.WorkspaceId&&x.LeadId==command.LeadId&&x.ConversionType=="LEAD_TO_CUSTOMER",cancellationToken);
        if(winner is not null)
        {
            if(winner.BusinessIntentFingerprint!=businessFingerprint)return Failure("LEAD_ALREADY_CONVERTED",409);
            return await ResumeAsync(winner.ScopeKey,trusted.MemberId,$"execution_{Guid.NewGuid():N}",cancellationToken,true);
        }
        var preparation=await leads.PrepareAsync(new(command.LeadId,command.RequestId,command.CorrelationId,command.ExpectedVersion),cancellationToken);
        if(!preparation.IsSuccess)return Failure(MapLeadCode(preparation.ErrorCode),preparation.ErrorStatus??409,current:preparation.CurrentVersion,expected:command.ExpectedVersion);
        string subjectId;long subjectVersion;
        if(normalizedType=="CONTACT")
        {
            var resolved=await contactSubjects.ResolveVisibleAsync(trusted,normalizedId,command.RequestId,command.CorrelationId,cancellationToken);
            if(resolved is null||!resolved.IsEligible)return Failure("LEAD_CONVERSION_SUBJECT_INVALID",404);
            subjectId=resolved.ContactId;subjectVersion=resolved.Version;
        }
        else
        {
            var resolved=await organizationSubjects.ResolveVisibleAsync(trusted,normalizedId,command.RequestId,command.CorrelationId,cancellationToken);
            if(resolved is null||!resolved.IsEligible)return Failure("LEAD_CONVERSION_SUBJECT_INVALID",404);
            subjectId=resolved.OrganizationId;subjectVersion=resolved.Version;
        }
        var now=timeProvider.GetUtcNow();
        var anchor=new LeadCustomerConversionAnchor(scope,trusted.WorkspaceId,command.LeadId,command.IdempotencyKey,requestFingerprint,businessFingerprint,
            command.ExpectedVersion,trusted.AccountId,trusted.MemberId,trusted.MembershipId,trusted.MemberId,command.CorrelationId,command.RequestId,
            normalizedType,subjectId,subjectVersion,preparation.OwnerId!,now);
        db.LeadCustomerConversionAnchors.Add(anchor);
        try{await db.SaveChangesAsync(cancellationToken);}
        catch(DbUpdateException)
        {
            db.ChangeTracker.Clear();winner=await db.LeadCustomerConversionAnchors.AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==trusted.WorkspaceId&&x.LeadId==command.LeadId&&x.ConversionType=="LEAD_TO_CUSTOMER",cancellationToken);
            if(winner is null)return Failure("INTERNAL_ERROR",503);
            if(winner.BusinessIntentFingerprint!=businessFingerprint)return Failure("LEAD_ALREADY_CONVERTED",409);
            return await ResumeAsync(winner.ScopeKey,trusted.MemberId,$"execution_{Guid.NewGuid():N}",cancellationToken,true);
        }
        return await ResumeAsync(scope,trusted.MemberId,$"execution_{Guid.NewGuid():N}",cancellationToken,false);
    }

    public async Task<int> ResumeDueAsync(string executorId,CancellationToken cancellationToken)
    {
        var due=timeProvider.GetUtcNow();var ids=await db.LeadCustomerConversionAnchors.AsNoTracking()
            .Where(x=>x.Stage!=LeadCustomerConversionStage.Completed&&x.Stage!=LeadCustomerConversionStage.ManualReview&&(x.NextRetryAt==null||x.NextRetryAt<=due))
            .OrderBy(x=>x.UpdatedAt).Select(x=>new{x.ScopeKey,x.WorkspaceId,x.CorrelationId}).Take(10).ToArrayAsync(cancellationToken);
        var count=0;
        foreach(var item in ids)
        {
            var decision=await serviceAccess.AuthorizeAsync(item.WorkspaceId,RecoveryPrincipal,RecoveryRequirement,item.CorrelationId,cancellationToken);
            if(!decision.IsAllowed){logger.LogWarning("Lead conversion recovery denied for workspace {WorkspaceId} and conversion scope {ScopeKey}",item.WorkspaceId,item.ScopeKey);continue;}
            var result=await ResumeAsync(item.ScopeKey,RecoveryPrincipal,$"recovery_{Guid.NewGuid():N}",cancellationToken,true);
            if(result.IsSuccess)count++;
        }
        return count;
    }

    private async Task<ConvertLeadToCustomerResult> ResumeAsync(string scope,string executor,string attemptId,CancellationToken ct,bool replay)
    {
        while(true)
        {
            db.ChangeTracker.Clear();var a=await db.LeadCustomerConversionAnchors.SingleOrDefaultAsync(x=>x.ScopeKey==scope,ct);
            if(a is null)return Failure("INTERNAL_ERROR",503);
            if(a.Stage==LeadCustomerConversionStage.Completed)return Success(JsonSerializer.Deserialize<LeadCustomerConversionResponse>(a.ResponseJson!,Json)! with{Outcome="REPLAYED"});
            if(a.Stage==LeadCustomerConversionStage.ManualReview)return Failure(a.LastErrorCode??"LEAD_CONVERSION_MANUAL_REVIEW",409);
            var now=timeProvider.GetUtcNow();if(a.HasActiveLease(now,attemptId))return Failure("LEAD_CONVERSION_IN_PROGRESS",409);
            a.AcquireLease(attemptId,executor,now,LeaseDuration);
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return Failure("LEAD_CONVERSION_IN_PROGRESS",409);}
            var trusted=new TrustedWorkspaceContext(a.WorkspaceId,a.OriginalAccountId,a.OriginalMemberId,a.OriginalMembershipId);
            if(a.Stage==LeadCustomerConversionStage.SubjectResolved)
            {
                var r=await customers.ResolveOrCreateAsync(new(trusted,a.SubjectType,a.SubjectId,a.LeadId,a.ConversionId,$"{a.ConversionId}:customer-resolve",a.RequestId,a.CorrelationId,a.OriginalPrincipalId,a.FrozenLeadOwnerId,executor),ct);
                if(!r.IsSuccess)return r.ErrorCode=="LIFECYCLE_CONFLICT"?await TerminalAsync(a,attemptId,"LIFECYCLE_CONFLICT",ct):await TransientAsync(a,attemptId,r.ErrorCode??"INTERNAL_ERROR",ct);
                await faults.AfterParticipantCommitAsync(LeadCustomerConversionFaultPoint.AfterCustomerCommit,ct);
                a.RecordCustomer(attemptId,r.CustomerId!,r.CustomerVersion!.Value,r.Resolution!,r.EmittedEventIds,r.AuditEvidenceIds,timeProvider.GetUtcNow());
            }
            else if(a.Stage==LeadCustomerConversionStage.CustomerResolved)
            {
                var r=await leads.RecordAsync(new(trusted,a.LeadId,a.CustomerId!,a.ConversionId,$"{a.ConversionId}:lead-record",a.RequestId,a.CorrelationId,a.OriginalPrincipalId,executor),ct);
                if(!r.IsSuccess)return r.ErrorCode=="INTERNAL_ERROR"?await TransientAsync(a,attemptId,r.ErrorCode,ct):await TerminalAsync(a,attemptId,r.ErrorCode??"LEAD_CONVERSION_MANUAL_REVIEW",ct);
                await faults.AfterParticipantCommitAsync(LeadCustomerConversionFaultPoint.AfterLeadCommit,ct);
                a.RecordLead(attemptId,r.LeadVersion!.Value,r.EmittedEventIds,r.AuditEvidenceIds,timeProvider.GetUtcNow());
            }
            else if(a.Stage==LeadCustomerConversionStage.LeadConversionRecorded)
            {
                var r=await customers.FinalizeAsync(new(trusted,a.CustomerId!,a.LeadId,a.ConversionId,$"{a.ConversionId}:customer-provenance-finalize",a.RequestId,a.CorrelationId,a.OriginalPrincipalId,executor,a.CustomerResolution!),ct);
                if(!r.IsSuccess)return await TransientAsync(a,attemptId,r.ErrorCode??"INTERNAL_ERROR",ct);
                await faults.AfterParticipantCommitAsync(LeadCustomerConversionFaultPoint.AfterProvenanceCommit,ct);
                a.RecordCustomerFinalization(attemptId,r.CustomerVersion!.Value,r.EmittedEventIds,r.AuditEvidenceIds,timeProvider.GetUtcNow());
            }
            else if(a.Stage==LeadCustomerConversionStage.ProvenanceFinalized)
            {
                var events=JsonSerializer.Deserialize<string[]>(a.EmittedEventIdsJson,Json)??[];var audits=JsonSerializer.Deserialize<string[]>(a.AuditEvidenceIdsJson,Json)??[];
                var completed=timeProvider.GetUtcNow();var response=new LeadCustomerConversionResponse(WorkflowIds.New("command"),a.CorrelationId,a.ConversionId,"LEAD_CUSTOMER_CONVERSION",a.LeadVersion!.Value,completed.UtcDateTime.ToString("O"),replay?"REPLAYED":"COMMITTED",new(a.ConversionId,a.LeadId,a.CustomerId!,a.CustomerResolution!,new(a.SubjectType,a.SubjectId),a.LeadVersion.Value,a.CustomerVersion!.Value){ContactId=a.SubjectType=="CONTACT"?a.SubjectId:null,OrganizationId=a.SubjectType=="ORGANIZATION_ACCOUNT"?a.SubjectId:null},[],events,audits);
                a.Complete(JsonSerializer.Serialize(response with{Outcome="COMMITTED"},Json),completed);await db.SaveChangesAsync(ct);return Success(response);
            }
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){return Failure("LEAD_CONVERSION_IN_PROGRESS",409);}
        }
    }

    private async Task<ConvertLeadToCustomerResult> TerminalAsync(LeadCustomerConversionAnchor a,string attempt,string code,CancellationToken ct){a.ManualReview(attempt,code,timeProvider.GetUtcNow());await db.SaveChangesAsync(ct);return Failure(code,409);}
    private async Task<ConvertLeadToCustomerResult> TransientAsync(LeadCustomerConversionAnchor a,string attempt,string code,CancellationToken ct){var now=timeProvider.GetUtcNow();a.Retry(attempt,code,now.AddMinutes(1),now);await db.SaveChangesAsync(ct);return Failure(code,503);}
    private static IReadOnlyDictionary<string,string[]>? Validate(ConvertLeadToCustomerRequest r){var f=new Dictionary<string,string[]>();var s=r.AccountSubject;if(s is null)f["accountSubject"]=["accountSubject is required."];else{if(s.Type is not("CONTACT" or "ORGANIZATION_ACCOUNT"))f["accountSubject.type"]=["Unsupported subject type."];if(s.Mode!="EXISTING")f["accountSubject.mode"]=["Only EXISTING subjects are admitted."];if(string.IsNullOrWhiteSpace(s.Id))f["accountSubject.id"]=["id is required for EXISTING."];if(s.Contact is not null)f["accountSubject.contact"]=["contact is forbidden for EXISTING."];if(r.Stakeholder is not null)f["stakeholder"]=["Stakeholder creation is not admitted."];}return f.Count==0?null:f;}
    private static string MapLeadCode(string? c)=>c=="RESOURCE_VERSION_CONFLICT"?"VERSION_CONFLICT":c??"INTERNAL_ERROR";
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static ConvertLeadToCustomerResult Success(LeadCustomerConversionResponse response)=>new(true,response);
    private static ConvertLeadToCustomerResult Failure(string code,int status,IReadOnlyDictionary<string,string[]>? fields=null,long? expected=null,long? current=null,string? idempotency=null)=>new(false,null,code,status,fields,expected,current,idempotency);
}
