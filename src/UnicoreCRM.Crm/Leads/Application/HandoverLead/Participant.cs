using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.HandoverLead;

internal sealed class Participant(LeadAuthorization authorization, ILeadsPersistence persistence,
    IWorkspaceMemberReferenceValidator members, IServiceAccessAuthorizer serviceAccess, TimeProvider time) : ILeadHandoverParticipant
{
    public async Task<LeadDocument?> ProjectAsync(LeadDocument document, string requestId, string correlationId, CancellationToken ct)
    {
        var permission = await authorization.AuthorizeAsync(LeadCapabilities.Handover, new(requestId, correlationId), ct);
        return permission.IsSuccess ? LeadFieldSecurity.Project(document, permission.Value!.Authorization) : null;
    }
    public async Task<LeadHandoverPreparation> AuthorizeAsync(PrepareLeadHandoverCommand command, CancellationToken ct)
    {
        var metadata = new LeadRequestMetadata(command.RequestId, command.CorrelationId);
        var permission = await authorization.AuthorizeAsync(LeadCapabilities.Handover, metadata, ct);
        if (!permission.IsSuccess) return Failed(permission.Error!);
        if (!LeadValidation.IsEntityId(command.LeadId)) return Failed(LeadErrors.NotFound());
        var access = permission.Value!;
        var lead = await persistence.ReadLeadAsync(access.Trusted.WorkspaceId, command.LeadId, ct);
        if (lead is null) return Failed(LeadErrors.NotFound());
        var denied = await authorization.EnforceRecordAsync(access, lead, "handoverLeadWithTasks", metadata, ct);
        if (denied is not null) return Failed(denied);
        if (command.RequiresOwnerWrite)
        {
            var fieldError = LeadAuthorization.EnforceFieldWrite(access, "ownerId");
            if (fieldError is not null) return Failed(fieldError);
        }
        return new(true, access.Trusted, lead.Profile.OwnerId, lead.Version);
    }

    public async Task<LeadHandoverPreparation> PrepareAsync(PrepareLeadHandoverCommand command, CancellationToken ct)
    {
        var authorized = await AuthorizeAsync(command, ct);
        if (!authorized.IsSuccess) return authorized;
        var permission = await authorization.AuthorizeAsync(LeadCapabilities.Handover, new(command.RequestId, command.CorrelationId), ct);
        if (!permission.IsSuccess) return Failed(permission.Error!);
        var fieldError = LeadAuthorization.EnforceFieldWrite(permission.Value!, "ownerId");
        if (fieldError is not null) return Failed(fieldError);
        var lead = await persistence.ReadLeadAsync(authorized.TrustedWorkspace!.WorkspaceId, command.LeadId, ct);
        if (lead is null) return Failed(LeadErrors.NotFound());
        if (lead.Version != command.ExpectedVersion) return Failed(LeadErrors.VersionConflict(lead.LeadId, command.ExpectedVersion, lead.Version));
        if (lead.ArchivedAt is not null || lead.Profile.OwnerId is null || lead.Profile.OwnerId == command.NewOwnerId
            || lead.PendingCustomerConversionId is not null || lead.PendingHandoverId is not null)
            return new(false, null, null, lead.Version, "LEAD_HANDOVER_INELIGIBLE", 409);
        if (!await members.IsActiveMemberAsync(authorized.TrustedWorkspace.WorkspaceId, command.NewOwnerId, ct))
            return new(false, null, null, null, "LEAD_OWNER_NOT_ASSIGNABLE", 422);
        return authorized;
    }

    public Task<LeadHandoverParticipantResult> ReserveAsync(LeadHandoverParticipantCommand command, CancellationToken ct) => MutateAsync(command, "reserve", ct);
    public Task<LeadHandoverParticipantResult> ResolveReservationOrFenceAsync(LeadHandoverParticipantCommand command, CancellationToken ct) => MutateAsync(command, "reserve", ct, fence: true);
    public Task<LeadHandoverParticipantResult> CompleteAsync(LeadHandoverParticipantCommand command, CancellationToken ct) => MutateAsync(command, "complete", ct);
    public Task<LeadHandoverParticipantResult> ReleaseAsync(LeadHandoverParticipantCommand command, CancellationToken ct) => MutateAsync(command, "release", ct);

    private async Task<LeadHandoverParticipantResult> MutateAsync(LeadHandoverParticipantCommand command, string stage, CancellationToken ct, bool fence = false)
    {
        var trusted = command.TrustedWorkspace;
        if (fence && command.ExecutorPrincipalId != "svc_lead_handover_recovery")
            return new(false, null, "ACCESS_DENIED", 403);
        if (fence || command.ExecutorPrincipalId != command.OriginalPrincipalId)
        {
            var service = await serviceAccess.AuthorizeAsync(trusted.WorkspaceId, command.ExecutorPrincipalId,
                AccessRequirement.ForCanonicalCapability("leads.handover.recover"), command.CorrelationId, ct);
            if (!service.IsAllowed) return new(false, null, "ACCESS_DENIED", 403);
        }
        await using var transaction = await persistence.BeginSerializableAsync(ct);
        var lead = await persistence.LoadLeadForClaimAsync(trusted.WorkspaceId, command.LeadId, ct);
        if (lead is null) return new(false, null, "RESOURCE_NOT_FOUND", 404);
        var scope = LeadCommandSupport.Fingerprint(new { trusted.WorkspaceId, command.HandoverId, stage });
        var fingerprint = LeadCommandSupport.Fingerprint(new { command.LeadId, command.HandoverId, command.PreviousOwnerId,
            command.NewOwnerId, command.Reason, command.OpenTaskPolicy, command.ExpectedLeadVersion, stage });
        var prior = await persistence.FindIdempotencyAsync(scope, ct);
        if (prior is not null)
        {
            if (prior.Fingerprint != fingerprint) return new(false, null, "IDEMPOTENCY_KEY_REUSED", 409);
            if (prior.Operation == "handoverLead:reserve:fenced") return new(false, null, "HANDOVER_RESERVATION_FENCED", 409);
            return new(true, LeadCommandSupport.Replay(prior));
        }
        if (stage == "reserve" && !fence)
        {
            var prepared = await PrepareAsync(new(command.LeadId, command.RequestId, command.CorrelationId,
                command.ExpectedLeadVersion, command.NewOwnerId), ct);
            if (!prepared.IsSuccess) return new(false, null, prepared.ErrorCode, prepared.ErrorStatus);
        }
        var before = lead.Version;
        var now = time.GetUtcNow();
        var changed = fence || (stage switch
        {
            "reserve" => lead.Version == command.ExpectedLeadVersion && lead.ReserveHandover(command.HandoverId, command.PreviousOwnerId, now),
            "complete" => lead.CompleteHandover(command.HandoverId, command.PreviousOwnerId, command.NewOwnerId, now),
            "release" => lead.ReleaseHandover(command.HandoverId, now),
            _ => false
        });
        if (!changed) return new(false, null, "LEAD_HANDOVER_INELIGIBLE", 409);
        var metadata = new LeadCommandMetadata(command.RequestId, command.CorrelationId, command.ParticipantKey, before,
            ActorId: command.ExecutorPrincipalId, ActorType: command.ExecutorPrincipalId == command.OriginalPrincipalId ? "HUMAN" : "WORKFLOW_RECOVERY",
            DelegatedSubjectId: command.OriginalPrincipalId, SourceReference: command.HandoverId);
        var evidence = new { command.HandoverId, command.LeadId, command.PreviousOwnerId, command.NewOwnerId,
            command.Reason, command.OpenTaskPolicy, command.OriginalPrincipalId, command.ExecutorPrincipalId,
            command.ParticipantKey, command.RequestId, command.CorrelationId, occurredAt = now, recordedAt = now };
        var response = LeadCommandSupport.RecordCommit(persistence, lead, trusted, metadata, fence ? "handoverLead:reserve:fenced" : $"handoverLead:{stage}",
            fence ? "LEAD_HANDOVER_RESERVATION_FENCED" : stage == "complete" ? "LEAD_HANDOVER_COMPLETED" : $"LEAD_HANDOVER_{stage.ToUpperInvariant()}",
            scope, command.LeadId, fingerprint, before, now, evidence, evidence);
        await persistence.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return fence ? new(false, null, "HANDOVER_RESERVATION_FENCED", 409) : new(true, response);
    }
    private static LeadHandoverPreparation Failed(LeadOperationError error) => new(false, null, null, error.CurrentVersion, error.Code, error.Status);
}
