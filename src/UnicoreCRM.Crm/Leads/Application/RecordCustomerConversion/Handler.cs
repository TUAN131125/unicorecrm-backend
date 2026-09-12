using System.Text.Json;
using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.RecordCustomerConversion;

internal sealed class Handler(LeadAuthorization authorization, ILeadsPersistence persistence, TimeProvider timeProvider)
    : ILeadCustomerConversionParticipant
{
    private const string Operation = "convertLeadToCustomer";

    public async Task<LeadCustomerConversionPreparation> AuthorizeAsync(PrepareLeadCustomerConversionCommand command, CancellationToken cancellationToken)
    {
        var metadata = new LeadRequestMetadata(command.RequestId, command.CorrelationId);
        var access = await authorization.AuthorizeAsync(LeadCapabilities.ConvertToCustomer, metadata, cancellationToken);
        if (!access.IsSuccess) return Failed(access.Error!);
        if (!LeadValidation.IsEntityId(command.LeadId)) return Failed(LeadErrors.NotFound());
        var lead = await persistence.ReadLeadAsync(access.Value!.Trusted.WorkspaceId, command.LeadId, cancellationToken);
        if (lead is null) return Failed(LeadErrors.NotFound());
        var denied = await authorization.EnforceRecordAsync(access.Value, lead, Operation, metadata, cancellationToken);
        if (denied is not null) return Failed(denied);
        var fieldError = LeadAuthorization.EnforceFieldWrite(access.Value, "customerRef");
        return fieldError is null
            ? new(true, access.Value.Trusted, lead.Profile.OwnerId, lead.Version, lead.Profile.DoNotCall, lead.Profile.DoNotEmail, null, null)
            : Failed(fieldError);
    }

    public async Task<LeadCustomerConversionPreparation> PrepareAsync(PrepareLeadCustomerConversionCommand command, CancellationToken cancellationToken)
    {
        var authorized = await AuthorizeAsync(command, cancellationToken);
        if (!authorized.IsSuccess) return authorized;
        var lead = await persistence.ReadLeadAsync(authorized.TrustedWorkspace!.WorkspaceId, command.LeadId, cancellationToken);
        if (lead is null) return Failed(LeadErrors.NotFound());
        if (lead.Version != command.ExpectedVersion) return Failed(LeadErrors.VersionConflict(lead.LeadId, command.ExpectedVersion, lead.Version));
        if (lead.ArchivedAt is not null || (lead.WorkState == LeadWorkState.Closed && lead.QualificationOutcome == LeadQualificationOutcome.Disqualified))
            return new(false, null, null, null, null, null, "LEAD_CONVERSION_INELIGIBLE", 409, lead.Version);
        return authorized;
    }

    public async Task<LeadCustomerConversionRecord> RecordAsync(RecordLeadCustomerConversionCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scope = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"{command.TrustedWorkspace.WorkspaceId}\n{Operation}\n{command.LeadId}\n{command.ParticipantKey}")));
        var fingerprint = LeadCommandSupport.Fingerprint(new { command.LeadId, command.CustomerId, command.WorkflowId });
        var prior = await persistence.FindIdempotencyAsync(scope, cancellationToken);
        if (prior is not null)
        {
            if (prior.Fingerprint != fingerprint) return Failure("IDEMPOTENCY_KEY_REUSED", 409);
            var replay = LeadCommandSupport.Replay(prior);
            return new(true, true, replay.Version, replay.CommandId, replay.EmittedEventIds, replay.AuditEvidenceIds, null, null);
        }
        var lead = await persistence.LoadLeadAsync(command.TrustedWorkspace.WorkspaceId, command.LeadId, cancellationToken);
        if (lead is null) return Failure("RESOURCE_NOT_FOUND", 404);
        var before = lead.Version;
        var result = lead.RecordCustomerConversion(command.CustomerId, timeProvider.GetUtcNow());
        if (result == LeadCustomerConversionRecordResult.ConflictingCustomer) return Failure("LEAD_ALREADY_CONVERTED", 409);
        if (result == LeadCustomerConversionRecordResult.Ineligible) return Failure("LEAD_CONVERSION_MANUAL_REVIEW", 409);
        if (result == LeadCustomerConversionRecordResult.Replayed)
            return new(true, true, lead.Version, null, [], [], null, null);
        var metadata = new LeadCommandMetadata(command.RequestId, command.CorrelationId, command.ParticipantKey, before,
            ActorId: command.ExecutorPrincipalId, ActorType: command.ExecutorPrincipalId == command.OriginalPrincipalId ? "HUMAN" : "WORKFLOW_RECOVERY",
            DelegatedSubjectId: command.OriginalPrincipalId, SourceReference: command.WorkflowId);
        var response = LeadCommandSupport.RecordCommit(persistence, lead, command.TrustedWorkspace, metadata, Operation,
            "LEAD_CUSTOMER_CONVERSION_RECORDED", scope, command.LeadId, fingerprint, before, timeProvider.GetUtcNow());
        try { await persistence.SaveChangesAsync(cancellationToken); }
        catch (LeadsPersistenceConcurrencyException) { return Failure("VERSION_CONFLICT", 412); }
        await transaction.CommitAsync(cancellationToken);
        return new(true, false, response.Version, response.CommandId, response.EmittedEventIds, response.AuditEvidenceIds, null, null);
    }

    private static LeadCustomerConversionPreparation Failed(LeadOperationError e) => new(false, null, null, null, null, null, e.Code, e.Status, e.CurrentVersion);
    private static LeadCustomerConversionRecord Failure(string code, int status) => new(false, false, null, null, [], [], code, status);
}
