using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Workflows.Atomic.Contracts;
using UnicoreCRM.Workflows.Atomic.Domain;
using UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;
using CustomerParticipant = UnicoreCRM.Crm.Customers.Contracts.ILeadCustomerConversionParticipant;
using LeadParticipant = UnicoreCRM.Crm.Leads.Contracts.ILeadCustomerConversionParticipant;

namespace UnicoreCRM.Workflows.Atomic.Application.ConvertLeadToCustomer;

internal interface ILeadCustomerConversionRecoveryRunner { Task<int> ResumeDueAsync(string executorId, CancellationToken cancellationToken); }

internal sealed class Handler(WorkflowsDbContext db, LeadParticipant leads, IContactCustomerSubjectParticipant contactSubjects,
    IOrganizationCustomerSubjectParticipant organizationSubjects,
    CustomerParticipant customers, TimeProvider timeProvider, ILogger<Handler> logger)
    : ILeadCustomerConversionWorkflow, ILeadCustomerConversionRecoveryRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Operation = "convertLeadToCustomer";

    public async Task<ConvertLeadToCustomerResult> ExecuteAsync(ConvertLeadToCustomerCommand command, CancellationToken cancellationToken)
    {
        var validation = Validate(command.Request);
        if (validation is not null) return Failure("VALIDATION_FAILED", 422, validation);
        var authorization = await leads.AuthorizeAsync(new(command.LeadId, command.RequestId, command.CorrelationId, command.ExpectedVersion), cancellationToken);
        if (!authorization.IsSuccess) return Failure(MapLeadCode(authorization.ErrorCode), authorization.ErrorStatus ?? 403, current: authorization.CurrentVersion, expected: command.ExpectedVersion);
        var trusted = authorization.TrustedWorkspace!; var subject = command.Request.AccountSubject!;
        var fingerprint = Hash(JsonSerializer.Serialize(new { command.LeadId, command.ExpectedVersion, subject, command.Request.Stakeholder }, Json));
        var scope = Hash($"{trusted.WorkspaceId}\n{Operation}\n{command.LeadId}\n{command.IdempotencyKey}");
        var anchor = await db.LeadCustomerConversionAnchors.SingleOrDefaultAsync(x=>x.ScopeKey==scope,cancellationToken);
        if (anchor is not null)
        {
            if(anchor.Fingerprint!=fingerprint) return Failure("IDEMPOTENCY_KEY_REUSED",409,idempotency:command.IdempotencyKey);
            return await ResumeAsync(anchor, trusted.MemberId, cancellationToken, replay:true);
        }
        var businessWinner = await db.LeadCustomerConversionAnchors.SingleOrDefaultAsync(x=>x.WorkspaceId==trusted.WorkspaceId&&x.LeadId==command.LeadId&&x.ConversionType=="LEAD_TO_CUSTOMER",cancellationToken);
        if (businessWinner is not null)
        {
            if (businessWinner.Fingerprint != fingerprint) return Failure("LEAD_ALREADY_CONVERTED",409);
            return await ResumeAsync(businessWinner,trusted.MemberId,cancellationToken,true);
        }
        var preparation = await leads.PrepareAsync(new(command.LeadId, command.RequestId, command.CorrelationId, command.ExpectedVersion), cancellationToken);
        if (!preparation.IsSuccess) return Failure(MapLeadCode(preparation.ErrorCode), preparation.ErrorStatus ?? 409, current: preparation.CurrentVersion, expected: command.ExpectedVersion);
        string authorizedSubjectId; long authorizedSubjectVersion;
        if(subject.Type=="CONTACT")
        {
            var resolved=await contactSubjects.ResolveVisibleAsync(trusted,subject.Id!,command.RequestId,command.CorrelationId,cancellationToken);
            if(resolved is null||!resolved.IsEligible)return Failure("LEAD_CONVERSION_SUBJECT_INVALID",404);
            authorizedSubjectId=resolved.ContactId;authorizedSubjectVersion=resolved.Version;
        }
        else
        {
            var resolved=await organizationSubjects.ResolveVisibleAsync(trusted,subject.Id!,command.RequestId,command.CorrelationId,cancellationToken);
            if(resolved is null||!resolved.IsEligible)return Failure("LEAD_CONVERSION_SUBJECT_INVALID",404);
            authorizedSubjectId=resolved.OrganizationId;authorizedSubjectVersion=resolved.Version;
        }
        var now=timeProvider.GetUtcNow();
        anchor=new(scope,trusted.WorkspaceId,command.LeadId,command.IdempotencyKey,fingerprint,command.ExpectedVersion,
            trusted.AccountId,trusted.MemberId,trusted.MembershipId,command.CorrelationId,command.RequestId,subject.Type!,subject.Mode!,subject.Id,
            subject.Contact is null?null:JsonSerializer.Serialize(subject.Contact,Json),command.Request.Stakeholder is null?null:JsonSerializer.Serialize(command.Request.Stakeholder,Json),now);
        anchor.Prepare(preparation.OwnerId,preparation.DoNotCall,preparation.DoNotEmail,now);
        anchor.RecordSubject(authorizedSubjectId,authorizedSubjectVersion,false,now);
        db.LeadCustomerConversionAnchors.Add(anchor);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch(DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner=await db.LeadCustomerConversionAnchors.SingleOrDefaultAsync(x=>x.WorkspaceId==trusted.WorkspaceId&&x.LeadId==command.LeadId&&x.ConversionType=="LEAD_TO_CUSTOMER",cancellationToken);
            if(winner is null) return Failure("INTERNAL_ERROR",503);
            if(winner.Fingerprint!=fingerprint) return Failure("LEAD_ALREADY_CONVERTED",409);
            return await ResumeAsync(winner,trusted.MemberId,cancellationToken,true);
        }
        return await ResumeAsync(anchor,trusted.MemberId,cancellationToken,false,preparation);
    }

    public async Task<int> ResumeDueAsync(string executorId, CancellationToken cancellationToken)
    {
        var due=timeProvider.GetUtcNow();
        var ids=await db.LeadCustomerConversionAnchors.AsNoTracking().Where(x=>x.Stage!=LeadCustomerConversionStage.Completed&&x.Stage!=LeadCustomerConversionStage.ManualReview&&(x.NextRetryAt==null||x.NextRetryAt<=due)).OrderBy(x=>x.UpdatedAt).Select(x=>x.ScopeKey).Take(10).ToArrayAsync(cancellationToken);
        var count=0;
        foreach(var id in ids)
        {
            db.ChangeTracker.Clear(); var anchor=await db.LeadCustomerConversionAnchors.SingleOrDefaultAsync(x=>x.ScopeKey==id,cancellationToken); if(anchor is null) continue;
            try { var result=await ResumeAsync(anchor,executorId,cancellationToken,true); if(result.IsSuccess) count++; }
            catch(DbUpdateConcurrencyException){ logger.LogDebug("Lead conversion {ConversionId} claimed by another worker",anchor.ConversionId); }
            catch(Exception ex){ anchor.Retry("INTERNAL_ERROR",due.AddMinutes(Math.Min(30,1<<Math.Min(anchor.AttemptCount,5))),executorId,due); await db.SaveChangesAsync(cancellationToken); logger.LogWarning(ex,"Lead conversion {ConversionId} scheduled for retry at stage {Stage}",anchor.ConversionId,anchor.Stage); }
        }
        return count;
    }

    private async Task<ConvertLeadToCustomerResult> ResumeAsync(LeadCustomerConversionAnchor a,string executor,CancellationToken ct,bool replay,LeadCustomerConversionPreparation? prepared=null)
    {
        if(a.Stage==LeadCustomerConversionStage.Completed) return Success(JsonSerializer.Deserialize<LeadCustomerConversionResponse>(a.ResponseJson!,Json)! with {Outcome="REPLAYED"});
        var trusted=new TrustedWorkspaceContext(a.WorkspaceId,a.OriginalAccountId,a.OriginalMemberId,a.OriginalMembershipId);
        if(a.Stage==LeadCustomerConversionStage.Created) return await TerminalAsync(a,"LEAD_CONVERSION_UNPREPARED",executor,ct);
        if(a.Stage==LeadCustomerConversionStage.Prepared)
        {
            string? id=null; long version=0; bool created=false;
            if(a.SubjectType=="CONTACT"&&a.SubjectMode=="EXISTING")
            {
                var r=executor==a.OriginalMemberId
                    ?await contactSubjects.ResolveVisibleAsync(trusted,a.SelectedSubjectId!,a.RequestId,a.CorrelationId,ct)
                    :await contactSubjects.ResolveAcceptedWorkflowAsync(trusted,a.SelectedSubjectId!,a.ConversionId,executor,ct);
                if(r is null||!r.IsEligible)return await TerminalAsync(a,"LEAD_CONVERSION_SUBJECT_INVALID",executor,ct);
                id=r.ContactId;
            }
            else if(a.SubjectType=="ORGANIZATION_ACCOUNT"&&a.SubjectMode=="EXISTING")
            {
                var r=executor==a.OriginalMemberId
                    ?await organizationSubjects.ResolveVisibleAsync(trusted,a.SelectedSubjectId!,a.RequestId,a.CorrelationId,ct)
                    :await organizationSubjects.ResolveAcceptedWorkflowAsync(trusted,a.SelectedSubjectId!,a.ConversionId,executor,ct);
                if(r is null||!r.IsEligible)return await TerminalAsync(a,"LEAD_CONVERSION_SUBJECT_INVALID",executor,ct); id=r.OrganizationId;version=r.Version;
            }
            else return await TerminalAsync(a,"LEAD_CONVERSION_SUBJECT_INVALID",executor,ct);
            a.RecordSubject(id!,version,created,timeProvider.GetUtcNow());await db.SaveChangesAsync(ct);
        }
        if(a.Stage==LeadCustomerConversionStage.SubjectResolved)
        {
            var r=await customers.ResolveOrCreateAsync(new(trusted,a.SubjectType,a.SubjectId!,a.LeadId,a.ConversionId,$"{a.ConversionId}:customer",a.RequestId,a.CorrelationId,a.OriginalMemberId),ct);
            if(!r.IsSuccess)return r.ErrorCode=="LIFECYCLE_CONFLICT"?await TerminalAsync(a,"LIFECYCLE_CONFLICT",executor,ct):await TransientAsync(a,r.ErrorCode??"INTERNAL_ERROR",executor,ct);
            a.RecordCustomer(r.CustomerId!,r.CustomerVersion!.Value,r.Resolution!,r.EmittedEventIds,r.AuditEvidenceIds,timeProvider.GetUtcNow());await db.SaveChangesAsync(ct);
        }
        if(a.Stage==LeadCustomerConversionStage.CustomerResolved)
        {
            if(a.StakeholderJson is not null)return await TerminalAsync(a,"LEAD_CONVERSION_VARIANT_NOT_ADMITTED",executor,ct);
            a.SkipOrRecordStakeholder(null,timeProvider.GetUtcNow());await db.SaveChangesAsync(ct);
        }
        if(a.Stage==LeadCustomerConversionStage.StakeholderResolved)
        {
            var r=await leads.RecordAsync(new(trusted,a.LeadId,a.CustomerId!,a.ConversionId,$"{a.ConversionId}:lead",a.RequestId,a.CorrelationId,a.OriginalMemberId,executor),ct);
            if(!r.IsSuccess)return r.ErrorCode=="INTERNAL_ERROR"?await TransientAsync(a,r.ErrorCode,executor,ct):await TerminalAsync(a,r.ErrorCode??"LEAD_CONVERSION_MANUAL_REVIEW",executor,ct);
            a.RecordLead(r.LeadVersion!.Value,r.EmittedEventIds,r.AuditEvidenceIds,timeProvider.GetUtcNow());await db.SaveChangesAsync(ct);
        }
        if(a.Stage==LeadCustomerConversionStage.LeadConversionRecorded)
        {
            var finalized=await customers.FinalizeAsync(new(trusted,a.CustomerId!,a.LeadId,a.ConversionId,a.RequestId,a.CorrelationId,a.OriginalMemberId,a.CustomerResolution!),ct);
            if(!finalized.IsSuccess)return await TransientAsync(a,finalized.ErrorCode??"INTERNAL_ERROR",executor,ct);
            a.RecordCustomerFinalization(finalized.CustomerVersion!.Value,finalized.EmittedEventIds,finalized.AuditEvidenceIds,timeProvider.GetUtcNow());await db.SaveChangesAsync(ct);
            var events=JsonSerializer.Deserialize<string[]>(a.EmittedEventIdsJson,Json)??[];var audits=JsonSerializer.Deserialize<string[]>(a.AuditEvidenceIdsJson,Json)??[];
            var now=timeProvider.GetUtcNow(); var response=new LeadCustomerConversionResponse(WorkflowIds.New("command"),a.CorrelationId,a.ConversionId,"LEAD_CUSTOMER_CONVERSION",a.LeadVersion!.Value,now.UtcDateTime.ToString("O"),replay?"REPLAYED":"COMMITTED",new(a.ConversionId,a.LeadId,a.CustomerId!,a.CustomerResolution!,new(a.SubjectType,a.SubjectId!),a.LeadVersion.Value,a.CustomerVersion!.Value){ContactId=a.SubjectType=="CONTACT"?a.SubjectId:null,OrganizationId=a.SubjectType=="ORGANIZATION_ACCOUNT"?a.SubjectId:null,StakeholderRelationshipId=a.StakeholderRelationshipId},[],events,audits);
            a.Complete(JsonSerializer.Serialize(response with{Outcome="COMMITTED"},Json),now);await db.SaveChangesAsync(ct);return Success(response);
        }
        return Failure("LEAD_CONVERSION_MANUAL_REVIEW",409);
    }

    private async Task<ConvertLeadToCustomerResult> TerminalAsync(LeadCustomerConversionAnchor a,string code,string executor,CancellationToken ct){a.ManualReview(code,executor,timeProvider.GetUtcNow());await db.SaveChangesAsync(ct);return Failure(code,409);}
    private async Task<ConvertLeadToCustomerResult> TransientAsync(LeadCustomerConversionAnchor a,string code,string executor,CancellationToken ct){var now=timeProvider.GetUtcNow();a.Retry(code,now.AddMinutes(1),executor,now);await db.SaveChangesAsync(ct);return Failure(code,503);}
    private static IReadOnlyDictionary<string,string[]>? Validate(ConvertLeadToCustomerRequest r){var f=new Dictionary<string,string[]>();var s=r.AccountSubject;if(s is null)f["accountSubject"]=["accountSubject is required."];else{if(s.Type is not("CONTACT" or "ORGANIZATION_ACCOUNT"))f["accountSubject.type"]=["Unsupported subject type."];if(s.Mode is not("EXISTING" or "NEW"))f["accountSubject.mode"]=["Unsupported subject mode."];if(s.Mode=="EXISTING"&&string.IsNullOrWhiteSpace(s.Id))f["accountSubject.id"]=["id is required for EXISTING."];if(s.Mode=="EXISTING"&&s.Contact is not null)f["accountSubject.contact"]=["contact is forbidden for EXISTING."];if(s.Mode=="NEW")f["accountSubject.mode"]=["NEW subject creation is not admitted by the current owner recovery contract."];if(r.Stakeholder is not null)f["stakeholder"]=["Stakeholder creation is not admitted by the current C6 recovery contract."];}return f.Count==0?null:f;}
    private static string MapLeadCode(string? c)=>c=="RESOURCE_VERSION_CONFLICT"?"VERSION_CONFLICT":c??"INTERNAL_ERROR";
    private static string Hash(string v)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(v)));
    private static ConvertLeadToCustomerResult Success(LeadCustomerConversionResponse r)=>new(true,r);
    private static ConvertLeadToCustomerResult Failure(string code,int status,IReadOnlyDictionary<string,string[]>? fields=null,long? expected=null,long? current=null,string? idempotency=null)=>new(false,null,code,status,fields,expected,current,idempotency);
}
