using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.Common;

internal sealed class LeadMutationExecution(
    ILeadsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<LeadOperationResult<LeadMutationResponse>> ExecuteAsync(
        TrustedWorkspaceContext trusted,
        string operation,
        string eventType,
        string leadId,
        LeadCommandMetadata metadata,
        string fingerprint,
        Func<Lead, DateTimeOffset, LeadOperationError?> mutate,
        Func<TrustedWorkspaceContext, CancellationToken, Task<LeadOperationError?>>? precondition,
        CancellationToken cancellationToken)
    {
        if (precondition is not null)
        {
            var error = await precondition(trusted, cancellationToken);
            if (error is not null)
                return LeadOperationResult<LeadMutationResponse>.Failure(error);
        }

        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = LeadCommandSupport.ScopeKey(trusted, operation, leadId, metadata);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = LeadCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? LeadOperationResult<LeadMutationResponse>.Success(LeadCommandSupport.Replay(existing))
                : LeadOperationResult<LeadMutationResponse>.Failure(replayError);
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
        return LeadOperationResult<LeadMutationResponse>.Success(response);
    }
}
