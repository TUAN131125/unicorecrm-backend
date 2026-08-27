using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Operations.Support.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Support.Application.Common;

/// <summary>
/// The single SupportCase mutation transaction.
///
/// <para>The semantic order is fixed: the caller has already been authenticated, resolved to a
/// trusted Workspace and authorized, and has normalized its request into a stable client-intent
/// fingerprint. This method then performs the idempotency lookup <em>before</em> anything else.
/// A committed key with matching intent replays immediately from stored evidence; a committed
/// key with different intent returns the canonical reuse error. Only when the key is new does
/// the command evaluate mutable owner/member state, load the aggregate, enforce the declared
/// If-Match contract, apply the slice mutation and stage Support-owned
/// audit/outbox/idempotency evidence - all inside one SERIALIZABLE transaction matching the
/// declared <c>SINGLE_SUPPORT_CASE_TRANSACTION</c> boundary.</para>
///
/// <para>Placing the precondition after the lookup is what makes a replay durable: a Workspace
/// member who is deactivated after a command commits must not turn that command's replay into
/// a validation failure. A stale version mutates nothing.</para>
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
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = SupportCommandSupport.ScopeKey(trusted, operation, caseId, metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            // A committed key is answered from stored evidence alone. No mutable foreign state is
            // consulted on this path, so a member deactivated after the original commit cannot
            // retroactively invalidate the replay.
            var replayError = SupportCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? SupportOperationResult<SupportCaseMutationResponse>.Success(SupportCommandSupport.Replay(existing))
                : SupportOperationResult<SupportCaseMutationResponse>.Failure(replayError);
        }

        // Only a genuinely new command evaluates current mutable owner/member state.
        if (precondition is not null)
        {
            var error = await precondition(trusted, cancellationToken);
            if (error is not null)
                return SupportOperationResult<SupportCaseMutationResponse>.Failure(error);
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
