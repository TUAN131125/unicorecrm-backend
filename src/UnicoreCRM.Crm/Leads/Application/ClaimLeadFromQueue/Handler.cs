using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.ClaimLeadFromQueue;

internal sealed record Command(string LeadId, ClaimLeadFromQueueRequest Request, LeadCommandMetadata Metadata);

internal sealed class Handler(LeadAuthorization authorization, ILeadsPersistence persistence,
    IWorkspaceMemberReferenceValidator memberValidator, TimeProvider timeProvider)
{
    internal async Task<LeadOperationResult<LeadMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var request = new LeadRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var claim = await authorization.AuthorizeAsync(LeadCapabilities.Claim, request, cancellationToken);
        if (!claim.IsSuccess) return Fail(claim.Error!);
        var access = claim.Value!;
        // The AccessControl decision combines normal read, queue capability and supported scopes.
        if (!access.Authorization.CanReadUnassigned) return Fail(LeadErrors.AccessDenied());
        if (!LeadValidation.IsEntityId(command.LeadId)) return Fail(LeadErrors.NotFound());
        var trusted = access.Trusted;
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        // Lock the single workspace-qualified Lead before key lookup. Distinct actors/keys cannot
        // both read null then convert shared locks to writes; the loser observes committed ownership.
        var lead = await persistence.LoadLeadForClaimAsync(trusted.WorkspaceId, command.LeadId, cancellationToken);
        if (lead is null) return Fail(LeadErrors.NotFound());
        var key = LeadCommandSupport.ScopeKey(trusted, "claimLeadFromQueue", lead.LeadId, command.Metadata);
        var fingerprint = LeadCommandSupport.Fingerprint(new { command.LeadId, command.Metadata.ExpectedVersion });
        var existing = await persistence.FindIdempotencyAsync(key, cancellationToken);
        if (existing is not null)
        {
            var scopeError = await authorization.EnforceRecordAsync(access, lead, "claimLeadFromQueue", request, cancellationToken);
            if (scopeError is not null) return Fail(scopeError);
            var replayError = LeadCommandSupport.ReplayError(existing, fingerprint);
            return replayError is not null ? Fail(replayError) : Success(LeadCommandSupport.Replay(existing), access);
        }
        // No owner/member data is returned on conflict, including a race loser with OWN scope.
        if (lead.Profile.OwnerId is not null) return Fail(new("LEAD_QUEUE_CLAIM_CONFLICT", 409, "Lead is no longer in the queue"));
        var recordError = await authorization.EnforceRecordAsync(access, lead, "claimLeadFromQueue", request, cancellationToken);
        if (recordError is not null) return Fail(recordError);
        if (lead.ArchivedAt is not null) return Fail(LeadErrors.AlreadyArchived(lead.LeadId));
        var fieldError = LeadAuthorization.EnforceFieldWrite(access, "ownerId");
        if (fieldError is not null) return Fail(fieldError);
        if (!await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, trusted.MemberId, cancellationToken))
            return Fail(new("LEAD_OWNER_NOT_ASSIGNABLE", 422, "Claim actor is not assignable"));
        var expected = command.Metadata.ExpectedVersion!.Value;
        if (lead.Version != expected) return Fail(LeadErrors.VersionConflict(lead.LeadId, expected, lead.Version));
        var now = timeProvider.GetUtcNow();
        if (!lead.Claim(trusted.MemberId, now)) return Fail(new("LIFECYCLE_CONFLICT", 409, "Lead cannot be claimed"));
        var response = LeadCommandSupport.RecordCommit(persistence, lead, trusted, command.Metadata,
            "claimLeadFromQueue", "LEAD_CLAIMED_FROM_QUEUE", key, lead.LeadId, fingerprint, expected, now,
            new { leadId = lead.LeadId, previousOwnerId = (string?)null, newOwnerId = trusted.MemberId,
                actorId = trusted.MemberId, workspaceId = trusted.WorkspaceId, occurredAt = now,
                command.Metadata.RequestId, command.Metadata.CorrelationId, command.Metadata.IdempotencyKey,
                resourceVersion = lead.Version });
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Success(response, access);
    }

    private static LeadOperationResult<LeadMutationResponse> Fail(LeadOperationError error) => LeadOperationResult<LeadMutationResponse>.Failure(error);
    private static LeadOperationResult<LeadMutationResponse> Success(LeadMutationResponse response, LeadAccess access) =>
        LeadOperationResult<LeadMutationResponse>.Success(response with { Result = LeadFieldSecurity.Project(response.Result, access.Authorization) });
}
