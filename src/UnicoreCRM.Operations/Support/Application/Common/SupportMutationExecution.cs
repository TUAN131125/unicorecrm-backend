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
///
/// <para>Record-access enforcement is deliberately <em>not</em> placed with the precondition. Record
/// scope is authorization, not a business precondition, and capability authorization already runs
/// ahead of the lookup, so a caller who no longer reaches a record must not be able to replay a
/// committed command against it. The guard therefore runs first, inside the same transaction,
/// against Support's own authoritative facts.</para>
/// </summary>
internal sealed class SupportMutationExecution(
    ISupportPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<SupportOperationResult<SupportCaseMutationResponse>> ExecuteAsync(
        SupportAccess access,
        string operation,
        string eventType,
        string caseId,
        SupportCommandMetadata metadata,
        string fingerprint,
        Func<SupportCase, DateTimeOffset, SupportOperationError?> mutate,
        Func<TrustedWorkspaceContext, CancellationToken, Task<SupportOperationError?>>? precondition,
        Func<SupportAccess, SupportCase, Task<SupportOperationError?>> recordGuard,
        CancellationToken cancellationToken)
    {
        var trusted = access.Trusted;
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);

        // The record-access guard runs before the idempotency lookup so a replay cannot bypass it.
        // A record the caller may not reach is reported as missing, exactly as an unknown record is.
        var guarded = await persistence.ReadCaseAsync(trusted.WorkspaceId, caseId, cancellationToken);
        if (guarded is null)
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(SupportErrors.NotFound());
        var guardError = await recordGuard(access, guarded);
        if (guardError is not null)
            return SupportOperationResult<SupportCaseMutationResponse>.Failure(guardError);

        var scopeKey = SupportCommandSupport.ScopeKey(trusted, operation, caseId, metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            // A committed key is answered from stored evidence alone. No mutable foreign state is
            // consulted on this path, so a member deactivated after the original commit cannot
            // retroactively invalidate the replay.
            var replayError = SupportCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? SupportOperationResult<SupportCaseMutationResponse>.Success(Project(SupportCommandSupport.Replay(existing), access))
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
        return SupportOperationResult<SupportCaseMutationResponse>.Success(Project(response, access));
    }

    /// <summary>
    /// Applies field security to the outgoing response. It is applied here rather than where the
    /// response is built because a replay returns a projection serialized under whatever policy was
    /// in force when the command committed; enforcing at the boundary makes the stored evidence
    /// unable to leak a value the caller's current policy withholds.
    /// </summary>
    private static SupportCaseMutationResponse Project(SupportCaseMutationResponse response, SupportAccess access) =>
        response with
        {
            Result = new SupportCaseMutationResult(
                SupportFieldSecurity.Project(response.Result.SupportCase, access.Authorization))
        };
}
