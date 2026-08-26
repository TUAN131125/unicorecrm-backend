using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Operations.Support.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Support.Application.Common;

/// <summary>
/// The single SupportCase mutation transaction. It enforces, in order: idempotent replay,
/// Workspace-scoped load, the declared If-Match optimistic-concurrency contract, the slice
/// mutation, then Support-owned audit/outbox/idempotency evidence - all inside one
/// SERIALIZABLE transaction, matching the declared
/// <c>SINGLE_SUPPORT_CASE_TRANSACTION</c> boundary. A stale version mutates nothing.
/// </summary>
internal sealed class SupportMutationExecution(
    ISupportPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<SupportOperationResult<SupportCaseMutationResponse>> ExecuteAsync(
        TrustedWorkspaceContext trusted,
        string operation,
        string eventType,
        string caseId,
        SupportCommandMetadata metadata,
        string fingerprint,
        Func<SupportCase, DateTimeOffset, SupportOperationError?> mutate,
        Func<TrustedWorkspaceContext, CancellationToken, Task<SupportOperationError?>>? precondition,
        CancellationToken cancellationToken)
    {
        if (precondition is not null)
        {
            var error = await precondition(trusted, cancellationToken);
            if (error is not null)
                return SupportOperationResult<SupportCaseMutationResponse>.Failure(error);
        }

        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = SupportCommandSupport.ScopeKey(trusted, operation, caseId, metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = SupportCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? SupportOperationResult<SupportCaseMutationResponse>.Success(SupportCommandSupport.Replay(existing))
                : SupportOperationResult<SupportCaseMutationResponse>.Failure(replayError);
        }

        var supportCase = await persistence.LoadCaseAsync(trusted.WorkspaceId, caseId, cancellationToken);
        if (supportCase is null)
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(SupportErrors.NotFound());
        var expectedVersion = metadata.ExpectedVersion!.Value;
        if (supportCase.Version != expectedVersion)
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(
                SupportErrors.VersionConflict(supportCase.CaseId, expectedVersion, supportCase.Version));
        var priorVersion = supportCase.Version;
        var now = timeProvider.GetUtcNow();
        var mutationError = mutate(supportCase, now);
        if (mutationError is not null)
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(mutationError);
        var response = SupportCommandSupport.RecordCommit(
            persistence,
            supportCase,
            trusted,
            metadata,
            operation,
            eventType,
            scopeKey,
            caseId,
            fingerprint,
            priorVersion,
            now);
        try
        {
            await persistence.SaveChangesAsync(cancellationToken);
        }
        catch (SupportPersistenceConcurrencyException)
        {
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(
                SupportErrors.VersionConflict(supportCase.CaseId, expectedVersion, supportCase.Version));
        }
        await transaction.CommitAsync(cancellationToken);
        return SupportOperationResult<SupportCaseMutationResponse>.Success(response);
    }
}
