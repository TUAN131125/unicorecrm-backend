using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.AssignLeadOwner;

internal sealed record Command(string LeadId, AssignLeadOwnerRequest Request, LeadCommandMetadata Metadata);

internal sealed class Handler(LeadAuthorization authorization, ILeadsPersistence persistence,
    IWorkspaceMemberReferenceValidator memberValidator, TimeProvider timeProvider)
{
    internal async Task<LeadOperationResult<LeadMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var request = new LeadRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var permission = await authorization.AuthorizeAsync(LeadCapabilities.Assign, request, cancellationToken);
        if (!permission.IsSuccess) return Fail(permission.Error!);
        var access = permission.Value!;
        if (!LeadValidation.IsEntityId(command.LeadId)) return Fail(LeadErrors.NotFound());
        var trusted = access.Trusted;
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        // Reuse the existing workspace-qualified UPDLOCK/HOLDLOCK loader so Claim and Assign
        // serialize on the same Lead. Claim's lock and its OWN-only exception remain unchanged.
        var lead = await persistence.LoadLeadForClaimAsync(trusted.WorkspaceId, command.LeadId, cancellationToken);
        if (lead is null) return Fail(LeadErrors.NotFound());
        var recordError = await authorization.EnforceRecordAsync(access, lead, "assignLeadOwner", request, cancellationToken);
        if (recordError is not null) return Fail(recordError);
        var fields = new Dictionary<string, string[]>();
        var ownerId = LeadValidation.Text(command.Request.OwnerId, "ownerId", 1, 128, true, fields);
        var reason = LeadValidation.Text(command.Request.Reason, "reason", 1, 2000, true, fields);
        if (string.IsNullOrWhiteSpace(reason)) return Fail(new("LEAD_ASSIGNMENT_REASON_REQUIRED", 422, "Assignment reason is required"));
        if (ownerId is null || !LeadValidation.IsEntityId(ownerId)) fields["ownerId"] = ["ownerId must be a valid member identifier."];
        if (fields.Count > 0) return Fail(LeadErrors.Validation(fields));
        var key = LeadCommandSupport.ScopeKey(trusted, "assignLeadOwner", lead.LeadId, command.Metadata);
        var fingerprint = LeadCommandSupport.Fingerprint(new { command.LeadId, ownerId, reason, command.Metadata.ExpectedVersion });
        var existing = await persistence.FindIdempotencyAsync(key, cancellationToken);
        if (existing is not null)
        {
            var replayError = LeadCommandSupport.ReplayError(existing, fingerprint);
            return replayError is not null ? Fail(replayError) : Success(LeadCommandSupport.Replay(existing), access);
        }
        var fieldError = LeadAuthorization.EnforceFieldWrite(access, "ownerId");
        if (fieldError is not null) return Fail(fieldError);
        if (lead.ArchivedAt is not null) return Fail(LeadErrors.AlreadyArchived(lead.LeadId));
        var expected = command.Metadata.ExpectedVersion!.Value;
        if (lead.Version != expected) return Fail(LeadErrors.VersionConflict(lead.LeadId, expected, lead.Version));
        if (!await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, ownerId!, cancellationToken))
            return Fail(new("LEAD_OWNER_NOT_ASSIGNABLE", 422, "Target owner is not assignable"));
        if (lead.Profile.OwnerId == ownerId) return Fail(new("LEAD_OWNER_ALREADY_ASSIGNED", 409, "Lead already has this owner"));
        var previousOwnerId = lead.Profile.OwnerId;
        var now = timeProvider.GetUtcNow();
        if (!lead.AssignOwner(ownerId!, now)) return Fail(new("LIFECYCLE_CONFLICT", 409, "Lead cannot be assigned"));
        var evidence = new { leadId = lead.LeadId, previousOwnerId, newOwnerId = ownerId, reason,
            actorId = trusted.MemberId, workspaceId = trusted.WorkspaceId, occurredAt = now,
            command.Metadata.RequestId, command.Metadata.CorrelationId, command.Metadata.IdempotencyKey,
            priorVersion = expected, newVersion = lead.Version };
        var response = LeadCommandSupport.RecordCommit(persistence, lead, trusted, command.Metadata,
            "assignLeadOwner", "LEAD_OWNER_ASSIGNED", key, lead.LeadId, fingerprint, expected, now, evidence, evidence);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Success(response, access);
    }

    private static LeadOperationResult<LeadMutationResponse> Fail(LeadOperationError error) => LeadOperationResult<LeadMutationResponse>.Failure(error);
    private static LeadOperationResult<LeadMutationResponse> Success(LeadMutationResponse response, LeadAccess access) =>
        LeadOperationResult<LeadMutationResponse>.Success(response with { Result = LeadFieldSecurity.Project(response.Result, access.Authorization) });
}
