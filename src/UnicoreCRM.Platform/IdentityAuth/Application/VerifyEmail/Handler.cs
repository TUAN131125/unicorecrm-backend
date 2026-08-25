using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.VerifyEmail;

/// <summary>
/// Consumes a six-digit email-verification code and activates the account.
///
/// Every way of failing to present the one currently usable code collapses to the same
/// <c>TOKEN_INVALID</c> answer: unknown address, an account that is not awaiting verification, no
/// outstanding challenge, a superseded or already consumed challenge, and a wrong code. Only two
/// states are reported distinctly, because the caller must be able to act on them: an expired code
/// and an exhausted attempt ceiling.
///
/// A wrong code commits its attempt increment, so the ceiling survives a caller that simply retries.
/// </summary>
internal sealed class Handler(
    IIdentityAuthPersistence persistence,
    IIdentityVerificationCodeProtector codes,
    IIdentityRequestFingerprinter fingerprinter,
    TimeProvider timeProvider)
{
    private const string Operation = "verifyEmail";

    internal async Task<OperationResult<UserAccountDocument>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(command);
        if (validation.Count != 0)
            return OperationResult<UserAccountDocument>.Failure(IdentityErrors.Validation(validation));

        var normalizedEmail = command.Email.Trim().ToUpperInvariant();
        var fingerprint = fingerprinter.Create(normalizedEmail, command.Code);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var prior = await persistence.FindIdempotencyAsync(Operation, command.Metadata.IdempotencyKey, cancellationToken);
        if (prior is not null)
        {
            if (!string.Equals(prior.Fingerprint, fingerprint, StringComparison.Ordinal))
                return OperationResult<UserAccountDocument>.Failure(IdentityErrors.IdempotencyReused());
            var replay = await persistence.FindAccountByIdAsync(prior.ResourceId, cancellationToken);
            return replay is null
                ? OperationResult<UserAccountDocument>.Failure(new OperationError("INTERNAL_ERROR", 500, "Internal server error"))
                : OperationResult<UserAccountDocument>.Success(IdentityProjection.Account(replay));
        }

        var now = timeProvider.GetUtcNow();
        var account = await persistence.FindAccountByEmailAsync(normalizedEmail, cancellationToken);
        if (account is not { Status: AccountStatus.PendingVerification })
            return OperationResult<UserAccountDocument>.Failure(IdentityErrors.VerificationCodeInvalid());

        var outstanding = await persistence.ListOutstandingEmailVerificationChallengesAsync(account.AccountId, cancellationToken);
        var challenge = outstanding.FirstOrDefault();
        if (challenge is null)
            return OperationResult<UserAccountDocument>.Failure(IdentityErrors.VerificationCodeInvalid());
        if (!challenge.HasAttemptsRemaining)
            return OperationResult<UserAccountDocument>.Failure(IdentityErrors.VerificationAttemptsExhausted());
        if (challenge.IsExpired(now))
            return OperationResult<UserAccountDocument>.Failure(IdentityErrors.VerificationCodeExpired());

        if (!codes.Matches(account.AccountId, command.Code, challenge.CodeHash))
        {
            challenge.RegisterFailedAttempt();
            persistence.AddAudit(new IdentityAuditRecord(Operation, "REJECTED", account.AccountId, command.Metadata.CorrelationId, now));
            persistence.AddSecurityEvent(new IdentitySecurityEvent("IDENTITY_EMAIL_VERIFICATION_FAILED", account.AccountId, command.Metadata.CorrelationId, now));
            await persistence.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return OperationResult<UserAccountDocument>.Failure(IdentityErrors.VerificationCodeInvalid());
        }

        challenge.Consume(now);
        // Defence in depth: only one challenge should ever be outstanding, so any other row here is
        // an anomaly and is closed rather than left usable.
        foreach (var stale in outstanding.Where(other => !ReferenceEquals(other, challenge)))
        {
            stale.Supersede(now);
        }

        // Every challenge closed above has had its code revoked, so no undelivered message may still
        // carry one to the holder's inbox. In practice the consumed challenge's own message was
        // delivered - that is how the caller knows the code - but a message left queued here would be
        // an email of a spent credential, so it is retired terminally and its payload dropped. A
        // message whose delivery attempt is already in flight is left alone: it cannot be recalled,
        // and the dispatcher records its own outcome when the attempt resolves.
        var undelivered = await persistence.ListUndeliveredEmailOutboxMessagesAsync(
            outstanding.Select(x => x.ChallengeId).ToArray(),
            cancellationToken);
        foreach (var message in undelivered.Where(message => !message.IsDeliveryInFlight(now)))
        {
            message.Cancel(now, EmailOutboxReasons.ChallengeConsumed);
        }

        account.MarkEmailVerified(now);
        persistence.AddIdempotency(new IdentityIdempotencyRecord(Operation, command.Metadata.IdempotencyKey, fingerprint, account.AccountId, now));
        persistence.AddAudit(new IdentityAuditRecord(Operation, "SUCCEEDED", account.AccountId, command.Metadata.CorrelationId, now));
        persistence.AddSecurityEvent(new IdentitySecurityEvent("IDENTITY_EMAIL_VERIFIED", account.AccountId, command.Metadata.CorrelationId, now));
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return OperationResult<UserAccountDocument>.Success(IdentityProjection.Account(account));
    }
}
