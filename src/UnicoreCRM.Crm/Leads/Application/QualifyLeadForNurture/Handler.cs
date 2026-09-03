using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.QualifyLeadForNurture;

/// <summary>
/// The Leads half of WF-10 NURTURE. It maps no route: positive qualification is reachable only
/// through the Workflows coordinator, and the generic <c>qualifyLead</c> operation stays retired.
/// </summary>
internal sealed class Handler(
    LeadAuthorization authorization,
    ILeadsPersistence persistence,
    LeadMutationExecution execution) : ILeadQualificationParticipant
{
    internal const string Operation = "qualifyLeadForNurture";
    private const string EventType = "LEAD_QUALIFIED_FOR_NURTURE";

    public async Task<LeadQualificationAuthorization> AuthorizeAsync(
        LeadQualificationAccessQuery query,
        CancellationToken cancellationToken)
    {
        var authorized = await ReadAuthorizedLeadAsync(query, cancellationToken);
        return authorized.IsSuccess
            ? new(true, authorized.Value.Access.Trusted, null, null)
            : new(false, null, authorized.Error!.Code, authorized.Error.Status);
    }

    public async Task<LeadQualificationPreparation> PrepareAsync(
        LeadQualificationPrepareCommand command,
        CancellationToken cancellationToken)
    {
        var authorized = await ReadAuthorizedLeadAsync(
            new(command.LeadId, command.RequestId, command.CorrelationId), cancellationToken);
        if (!authorized.IsSuccess)
            return Failed(authorized.Error!);
        var (access, lead) = authorized.Value;

        if (lead.Version != command.ExpectedVersion)
            return Failed(LeadErrors.VersionConflict(lead.LeadId, command.ExpectedVersion, lead.Version));

        // Both frozen business preconditions, re-evaluated against stored state at command time.
        // The progressive profile is checked here rather than inferred from VERIFYING, because
        // replaceLeadProfile can leave a VERIFYING Lead incomplete.
        if (lead.WorkState != LeadWorkState.Verifying || !lead.Profile.HasProgressiveProfile())
            return Failed(LeadErrors.InvalidTransition(lead.LeadId));

        return new LeadQualificationPreparation(
            true,
            access.Trusted,
            lead.Profile.OwnerId,
            lead.Version,
            null,
            null,
            null,
            lead.Profile.DoNotCall,
            lead.Profile.DoNotEmail);
    }

    private async Task<LeadOperationResult<(LeadAccess Access, Lead Lead)>> ReadAuthorizedLeadAsync(
        LeadQualificationAccessQuery query,
        CancellationToken cancellationToken)
    {
        var metadata = new LeadRequestMetadata(query.RequestId, query.CorrelationId);
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Qualify, metadata, cancellationToken);
        if (!access.IsSuccess)
            return LeadOperationResult<(LeadAccess, Lead)>.Failure(access.Error!);
        if (!LeadValidation.IsEntityId(query.LeadId))
            return LeadOperationResult<(LeadAccess, Lead)>.Failure(LeadErrors.NotFound());

        var lead = await persistence.ReadLeadAsync(access.Value!.Trusted.WorkspaceId, query.LeadId, cancellationToken);
        if (lead is null)
            return LeadOperationResult<(LeadAccess, Lead)>.Failure(LeadErrors.NotFound());

        // Reuse the canonical Leads record guard; no lifecycle or version fact is disclosed first.
        var denied = await authorization.EnforceRecordAsync(access.Value, lead, Operation, metadata, cancellationToken);
        return denied is null
            ? LeadOperationResult<(LeadAccess, Lead)>.Success((access.Value, lead))
            : LeadOperationResult<(LeadAccess, Lead)>.Failure(denied);
    }

    public async Task<LeadQualificationClosure> CloseForNurtureAsync(
        LeadQualificationCloseCommand command,
        CancellationToken cancellationToken)
    {
        var metadata = new LeadRequestMetadata(command.RequestId, command.CorrelationId);
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Qualify, metadata, cancellationToken);
        if (!access.IsSuccess)
            return ClosureFailed(access.Error!);

        var commandMetadata = new LeadCommandMetadata(
            command.RequestId,
            command.CorrelationId,
            command.IdempotencyKey,
            command.ExpectedVersion,
            IdempotencyScopeActorId: command.IdempotencyScopeActorId);

        // The resolved contactId is part of the fingerprint, so replaying this key against a
        // different resolved relationship is a genuine idempotency conflict rather than a silent
        // re-point of the conversion. If-Match remains an executor precondition for a fresh close,
        // not part of this workflow participant's immutable intent.
        var fingerprint = LeadCommandSupport.Fingerprint(new
        {
            command.LeadId,
            command.ContactId,
            Outcome = "NURTURE"
        });

        var result = await execution.ExecuteAsync(
            access.Value!,
            Operation,
            EventType,
            command.LeadId,
            commandMetadata,
            fingerprint,
            (lead, now) => lead.QualifyForNurture(command.ContactId, now)
                ? null
                : LeadErrors.InvalidTransition(lead.LeadId),
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, Operation, metadata, cancellationToken),
            recordAccess => LeadAuthorization.EnforceFieldWrite(
                recordAccess, "leadWorkState", "qualificationOutcome", "relationshipRef"),
            cancellationToken);

        return result.IsSuccess
            ? new LeadQualificationClosure(
                true,
                result.Value!.Outcome,
                result.Value.Version,
                null,
                null,
                null,
                null,
                null,
                result.Value.CommandId,
                result.Value.OccurredAt,
                result.Value.EmittedEventIds,
                result.Value.AuditEvidenceIds,
                result.Value.CorrelationId)
            : ClosureFailed(result.Error!);
    }

    private static LeadQualificationPreparation Failed(LeadOperationError error) =>
        new(false, null, null, error.CurrentVersion, error.Code, error.Status, error.FieldErrors);

    private static LeadQualificationClosure ClosureFailed(LeadOperationError error) =>
        new(false, null, null, error.Code, error.Status, error.ExpectedVersion, error.CurrentVersion, error.FieldErrors);
}
