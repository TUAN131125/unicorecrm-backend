using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.Common;

internal sealed class DealMutationExecution(
    IDealsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<DealOperationResult<DealMutationResponse>> ExecuteAsync(
        DealAccess access,
        string operation,
        string eventType,
        string dealId,
        DealCommandMetadata metadata,
        string fingerprint,
        Func<Deal, DateTimeOffset, DealOperationError?> mutate,
        Func<TrustedWorkspaceContext, CancellationToken, Task<DealOperationError?>>? precondition,
        Func<DealAccess, Deal, Task<DealOperationError?>> recordGuard,
        CancellationToken cancellationToken)
    {
        var trusted = access.Trusted;
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);

        // The record-access guard runs before the idempotency lookup so a replay cannot bypass it.
        // Record scope is current authorization, not a business precondition, so a caller who no
        // longer reaches a deal must not be able to replay a committed command against it.
        var guarded = await persistence.ReadDealAsync(trusted.WorkspaceId, dealId, cancellationToken);
        if (guarded is null)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.NotFound());
        var guardError = await recordGuard(access, guarded);
        if (guardError is not null)
            return DealOperationResult<DealMutationResponse>.Failure(guardError);

        var scopeKey = DealCommandSupport.ScopeKey(trusted, operation, dealId, metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = DealCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? DealOperationResult<DealMutationResponse>.Success(Project(DealCommandSupport.Replay(existing), access))
                : DealOperationResult<DealMutationResponse>.Failure(replayError);
        }

        // Only a genuinely new command evaluates current mutable owner/member state. A committed
        // replay is answered from stored evidence alone, so a member deactivated after the original
        // commit cannot retroactively turn that command's replay into a validation failure.
        if (precondition is not null)
        {
            var preconditionError = await precondition(trusted, cancellationToken);
            if (preconditionError is not null)
                return DealOperationResult<DealMutationResponse>.Failure(preconditionError);
        }

        var deal = await persistence.LoadDealAsync(trusted.WorkspaceId, dealId, cancellationToken);
        if (deal is null)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.NotFound());
        var expectedVersion = metadata.ExpectedVersion!.Value;
        if (deal.Version != expectedVersion)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.VersionConflict(deal.DealId, expectedVersion, deal.Version));
        var priorVersion = deal.Version;
        var now = timeProvider.GetUtcNow();
        var mutationError = mutate(deal, now);
        if (mutationError is not null)
            return DealOperationResult<DealMutationResponse>.Failure(mutationError);
        var response = DealCommandSupport.RecordCommit(
            persistence,
            deal,
            trusted,
            metadata,
            operation,
            eventType,
            scopeKey,
            dealId,
            fingerprint,
            priorVersion,
            now);
        try
        {
            await persistence.SaveChangesAsync(cancellationToken);
        }
        catch (DealsPersistenceConcurrencyException)
        {
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.VersionConflict(deal.DealId, expectedVersion, deal.Version));
        }
        await transaction.CommitAsync(cancellationToken);
        return DealOperationResult<DealMutationResponse>.Success(Project(response, access));
    }

    /// <summary>
    /// Applies field security to the outgoing response. It is applied at the boundary because a
    /// replay returns a projection serialized under whatever policy was in force when the command
    /// committed; enforcing here makes stored evidence unable to leak a currently withheld value.
    /// </summary>
    private static DealMutationResponse Project(DealMutationResponse response, DealAccess access) =>
        response with
        {
            Result = new DealMutationResult(DealFieldSecurity.Project(response.Result.Deal, access.Authorization))
        };
}
