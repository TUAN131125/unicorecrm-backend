using System.Text.Json;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;

namespace UnicoreCRM.Crm.Customers.Application.ResolveForLeadConversion;

internal sealed class Participant(ICustomersPersistence persistence, TimeProvider timeProvider) : ILeadCustomerConversionParticipant
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<ResolveLeadConversionCustomerResult> ResolveOrCreateAsync(ResolveLeadConversionCustomerCommand command, CancellationToken cancellationToken)
    {
        var scope = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"{command.TrustedWorkspace.WorkspaceId}\nleadConversionCustomer\n{command.ParticipantKey}")));
        var fingerprint = CustomerMutationSupport.Fingerprint(new { command.SubjectType, command.SubjectId, command.SourceLeadId, command.WorkflowId });
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var prior = await persistence.FindIdempotencyAsync(scope, cancellationToken);
        if (prior is not null)
        {
            if (prior.Fingerprint != fingerprint) return Failure("IDEMPOTENCY_KEY_REUSED", 409);
            var stored = JsonSerializer.Deserialize<StoredResult>(prior.ResponseJson, Json)!;
            return new(true, stored.CustomerId, stored.CustomerVersion, stored.Resolution, stored.EventIds, stored.AuditIds, null, null);
        }
        var existing = await persistence.LoadBySubjectAsync(command.TrustedWorkspace.WorkspaceId, command.SubjectType, command.SubjectId, cancellationToken);
        if (existing?.Status == "ARCHIVED") return Failure("LIFECYCLE_CONFLICT", 409);
        var now = timeProvider.GetUtcNow();
        Customer customer; string resolution; string[] events; string[] audits;
        if (existing is not null) { customer = existing; resolution = "REUSED"; events = []; audits = []; }
        else
        {
            customer = new Customer(command.TrustedWorkspace.WorkspaceId, command.LeadOwnerId, command.SubjectType, command.SubjectId,
                new CustomerProfile { CreatedFromEvidenceId = command.WorkflowId, ConversionPolicyVersion = "LEAD_CUSTOMER_V1",
                    ConversionCorrelationId = command.CorrelationId, SourceSystem = "LEAD_CONVERSION" }, now);
            persistence.AddCustomer(customer);
            var audit = new CustomerAuditRecord("resolveOrCreateForLeadConversion", command.TrustedWorkspace.WorkspaceId, command.ExecutorPrincipalId, customer.CustomerId, command.RequestId, command.CorrelationId, customer.Version, now);
            var message = new CustomerOutboxMessage("CUSTOMER_CREATED_FROM_LEAD", customer.CustomerId, command.TrustedWorkspace.WorkspaceId, command.CorrelationId,
                JsonSerializer.Serialize(new { customerId = customer.CustomerId, sourceLeadId = command.SourceLeadId, workflowId = command.WorkflowId }, Json), now);
            persistence.AddAudit(audit); persistence.AddOutbox(message); resolution = "CREATED"; events = [message.EventId]; audits = [audit.AuditId];
        }
        var committed = new StoredResult(customer.CustomerId, customer.Version, resolution,events,audits);
        persistence.AddLeadConversionProvenance(new CustomerLeadConversionProvenance(command.TrustedWorkspace.WorkspaceId,
            command.WorkflowId,customer.CustomerId,command.SourceLeadId,"LEAD_CUSTOMER_V1",command.CorrelationId,
            command.OriginalPrincipalId,resolution,now));
        persistence.AddIdempotency(new CustomerIdempotencyRecord(scope, command.TrustedWorkspace.WorkspaceId,
            "resolveOrCreateForLeadConversion", command.ExecutorPrincipalId, command.SubjectId, command.ParticipantKey,
            fingerprint, JsonSerializer.Serialize(committed, Json), now));
        try { await persistence.SaveChangesAsync(cancellationToken); }
        catch (CustomerBusinessKeyConflictException) { return Failure("INTERNAL_ERROR", 503); }
        catch (CustomersPersistenceConcurrencyException) { return Failure("INTERNAL_ERROR", 503); }
        await transaction.CommitAsync(cancellationToken);
        return new(true, customer.CustomerId, customer.Version, resolution, events, audits);
    }
    public async Task<ResolveLeadConversionCustomerResult> FinalizeAsync(FinalizeLeadConversionCustomerCommand command,CancellationToken cancellationToken)
    {
        await using var transaction=await persistence.BeginSerializableAsync(cancellationToken);
        var provenance=await persistence.LoadLeadConversionProvenanceAsync(command.TrustedWorkspace.WorkspaceId,command.WorkflowId,cancellationToken);
        var customer=await persistence.LoadCustomerAsync(command.TrustedWorkspace.WorkspaceId,command.CustomerId,cancellationToken);
        if(provenance is null||customer is null||provenance.SourceLeadId!=command.SourceLeadId)return Failure("INTERNAL_ERROR",503);
        if(provenance.CompletedAt is not null)return new(true,customer.CustomerId,customer.Version,command.Resolution,
            provenance.CompletionEventId is null?[]:[provenance.CompletionEventId],provenance.CompletionAuditId is null?[]:[provenance.CompletionAuditId],null,null);
        var now=timeProvider.GetUtcNow();
        var audit=new CustomerAuditRecord("completeLeadCustomerConversion",command.TrustedWorkspace.WorkspaceId,command.ExecutorPrincipalId,customer.CustomerId,command.RequestId,command.CorrelationId,customer.Version,now);
        var message=new CustomerOutboxMessage("LEAD_CUSTOMER_CONVERSION_COMPLETED",customer.CustomerId,command.TrustedWorkspace.WorkspaceId,command.CorrelationId,JsonSerializer.Serialize(new{command.WorkflowId,command.SourceLeadId,command.Resolution},Json),now);
        provenance.Complete(now,command.ExecutorPrincipalId,message.EventId,audit.AuditId);persistence.AddAudit(audit);persistence.AddOutbox(message);
        try{await persistence.SaveChangesAsync(cancellationToken);}catch(CustomersPersistenceConcurrencyException){return Failure("INTERNAL_ERROR",503);}
        await transaction.CommitAsync(cancellationToken);return new(true,customer.CustomerId,customer.Version,command.Resolution,[message.EventId],[audit.AuditId]);
    }
    private sealed record StoredResult(string CustomerId, long CustomerVersion, string Resolution,string[] EventIds,string[] AuditIds);
    private static ResolveLeadConversionCustomerResult Failure(string code, int status) => new(false, null, null, null, [], [], code, status);
}
