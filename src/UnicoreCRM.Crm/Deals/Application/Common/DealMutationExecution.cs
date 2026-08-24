using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.Common;

internal sealed class DealMutationExecution(
    IDealsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<DealOperationResult<DealMutationResponse>> ExecuteAsync(
        TrustedWorkspaceContext trusted,
        string operation,
        string eventType,
        string dealId,
        DealCommandMetadata metadata,
        string fingerprint,
        Func<Deal, DateTimeOffset, DealOperationError?> mutate,
        Func<TrustedWorkspaceContext, CancellationToken, Task<DealOperationError?>>? precondition,
        CancellationToken cancellationToken)
    {
        if (precondition is not null)
        {
            var error = await precondition(trusted, cancellationToken);
            if (error is not null)
                return DealOperationResult<DealMutationResponse>.Failure(error);
        }

        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = DealCommandSupport.ScopeKey(trusted, operation, dealId, metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = DealCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? DealOperationResult<DealMutationResponse>.Success(DealCommandSupport.Replay(existing))
                : DealOperationResult<DealMutationResponse>.Failure(replayError);
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
        return DealOperationResult<DealMutationResponse>.Success(response);
    }
}
