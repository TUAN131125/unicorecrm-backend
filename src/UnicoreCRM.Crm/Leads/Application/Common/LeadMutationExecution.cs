using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.Common;

internal sealed class LeadMutationExecution(
    ILeadsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<LeadOperationResult<LeadMutationResponse>> ExecuteAsync(
        LeadAccess access,
        string operation,
        string eventType,
        string leadId,
        LeadCommandMetadata metadata,
        string fingerprint,
        Func<Lead, DateTimeOffset, LeadOperationError?> mutate,
        Func<TrustedWorkspaceContext, CancellationToken, Task<LeadOperationError?>>? precondition,
        Func<LeadAccess, Lead, Task<LeadOperationError?>> recordGuard,
        CancellationToken cancellationToken)
    {
        var trusted = access.Trusted;
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);

        // The record-access guard runs before the idempotency lookup so a replay cannot bypass it.
        // Record scope is current authorization, not a business precondition, so a caller who no
        // longer reaches a lead must not be able to replay a committed command against it.
        var guarded = await persistence.ReadLeadAsync(trusted.WorkspaceId, leadId, cancellationToken);
        if (guarded is null)
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.NotFound());
        var guardError = await recordGuard(access, guarded);
        if (guardError is not null)
            return LeadOperationResult<LeadMutationResponse>.Failure(guardError);

        var scopeKey = LeadCommandSupport.ScopeKey(trusted, operation, leadId, metadata);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = LeadCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? LeadOperationResult<LeadMutationResponse>.Success(Project(LeadCommandSupport.Replay(existing), access))
                : LeadOperationResult<LeadMutationResponse>.Failure(replayError);
        }

        // Only a genuinely new command evaluates current mutable owner/member state. A committed
        // replay is answered from stored evidence alone, so a member deactivated after the original
        // commit cannot retroactively turn that command's replay into a validation failure.
        if (precondition is not null)
        {
            var preconditionError = await precondition(trusted, cancellationToken);
            if (preconditionError is not null)
                return LeadOperationResult<LeadMutationResponse>.Failure(preconditionError);
        }

        var lead = await persistence.LoadLeadAsync(trusted.WorkspaceId, leadId, cancellationToken);
        if (lead is null)
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.NotFound());
        var expectedVersion = metadata.ExpectedVersion!.Value;
        if (lead.Version != expectedVersion)
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.VersionConflict(lead.LeadId, expectedVersion, lead.Version));
        var priorVersion = lead.Version;
        var now = timeProvider.GetUtcNow();
        var mutationError = mutate(lead, now);
        if (mutationError is not null)
            return LeadOperationResult<LeadMutationResponse>.Failure(mutationError);
        var response = LeadCommandSupport.RecordCommit(
            persistence,
            lead,
            trusted,
            metadata,
            operation,
            eventType,
            scopeKey,
            leadId,
            fingerprint,
            priorVersion,
            now);
        try
        {
            await persistence.SaveChangesAsync(cancellationToken);
        }
        catch (LeadsPersistenceConcurrencyException)
        {
            return LeadOperationResult<LeadMutationResponse>.Failure(LeadErrors.VersionConflict(lead.LeadId, expectedVersion, lead.Version));
        }
        await transaction.CommitAsync(cancellationToken);
        return LeadOperationResult<LeadMutationResponse>.Success(Project(response, access));
    }

    /// <summary>
    /// Applies field security to the outgoing response. It is applied at the boundary because a
    /// replay returns a projection serialized under whatever policy was in force when the command
    /// committed; enforcing here makes stored evidence unable to leak a currently withheld value.
    /// </summary>
    private static LeadMutationResponse Project(LeadMutationResponse response, LeadAccess access) =>
        response with { Result = LeadFieldSecurity.Project(response.Result, access.Authorization) };
}
