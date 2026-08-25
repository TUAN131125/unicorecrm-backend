using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.RequestEmailVerification;

/// <summary>
/// Issues or re-issues an email-verification code for an account that is still awaiting
/// verification.
///
/// The response is deliberately uniform. A contract-valid request is accepted with the same shape
/// whether the address belongs to no account, to an account that is already active, to a suspended
/// account, or to an account whose resend cooldown has not elapsed. Nothing in the success path
/// discloses which of those was true, and the resend cooldown is enforced by silently declining to
/// issue a new code rather than by an observable rejection.
///
/// The one case that is not uniform is a configured-but-failing email boundary: the caller is told
/// the delivery could not happen instead of being told a code is on its way that never was.
///
/// A resend also declines silently while the account's current code is mid-delivery. Replacing a code
/// revokes it, and a code that has already been handed to the provider cannot be revoked, so issuing
/// there would be the one thing the outbox must never do: deliver a credential the system has already
/// invalidated. The claim is bounded by the delivery lease, so asking again shortly afterwards works.
/// </summary>
internal sealed class Handler(
    IIdentityAuthPersistence persistence,
    EmailVerificationChallengeIssuer issuer,
    IIdentityEmailDispatchTrigger dispatchTrigger,
    IIdentityRequestFingerprinter fingerprinter,
    TimeProvider timeProvider)
{
    private const string Operation = "requestEmailVerification";

    internal async Task<OperationResult<EmailVerificationRequestAcceptedResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(command);
        if (validation.Count != 0)
            return OperationResult<EmailVerificationRequestAcceptedResponse>.Failure(IdentityErrors.Validation(validation));

        var normalizedEmail = command.Email.Trim().ToUpperInvariant();
        var fingerprint = fingerprinter.Create(normalizedEmail);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var prior = await persistence.FindIdempotencyAsync(Operation, command.Metadata.IdempotencyKey, cancellationToken);
        if (prior is not null)
        {
            return string.Equals(prior.Fingerprint, fingerprint, StringComparison.Ordinal)
                ? OperationResult<EmailVerificationRequestAcceptedResponse>.Success(new EmailVerificationRequestAcceptedResponse(prior.ResourceId, prior.CreatedAt))
                : OperationResult<EmailVerificationRequestAcceptedResponse>.Failure(IdentityErrors.IdempotencyReused());
        }

        var now = timeProvider.GetUtcNow();
        // The acceptance identifier is owner-assigned and carries no account identity, so the same
        // response shape is available whether or not an account exists.
        var requestId = IdentityIds.New("evr");
        var account = await persistence.FindAccountByEmailAsync(normalizedEmail, cancellationToken);
        var outcome = "ACCEPTED_NO_ACTION";
        if (account is { Status: AccountStatus.PendingVerification })
        {
            var outstanding = await persistence.ListOutstandingEmailVerificationChallengesAsync(account.AccountId, cancellationToken);
            // The cooldown is measured against the most recent outstanding challenge, not against
            // the most recent *usable* one. Measuring usability here would let a caller spend the
            // attempt ceiling and immediately buy a fresh code, turning the ceiling into an
            // unthrottled guessing loop. An expired challenge is no obstacle either way, because its
            // resend window has necessarily elapsed long before its expiry.
            var current = outstanding.FirstOrDefault();
            if (current is null || current.CanResend(now))
            {
                try
                {
                    await issuer.IssueAsync(account, command.Metadata.CorrelationId, now, cancellationToken);
                    outcome = "ISSUED";
                }
                catch (IdentityEmailSenderUnavailableException)
                {
                    return OperationResult<EmailVerificationRequestAcceptedResponse>.Failure(IdentityErrors.EmailDeliveryUnavailable());
                }
                catch (IdentityEmailDeliveryInFlightException)
                {
                    // The current code is being handed to the provider right now. Issuing a
                    // replacement would revoke a code that is already on its way, and the holder would
                    // then receive a credential the system had just invalidated. Nothing is issued and
                    // the cooldown is not restarted, so the caller may simply ask again once the claim
                    // - bounded by the delivery lease - has resolved. The response stays the same
                    // uniform acceptance as every other non-issuing outcome.
                    outcome = "ACCEPTED_DELIVERY_IN_FLIGHT";
                }
            }
            else
            {
                outcome = "ACCEPTED_COOLDOWN";
            }
        }

        persistence.AddIdempotency(new IdentityIdempotencyRecord(Operation, command.Metadata.IdempotencyKey, fingerprint, requestId, now));
        persistence.AddAudit(new IdentityAuditRecord(Operation, outcome, account?.AccountId, command.Metadata.CorrelationId, now));
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (outcome == "ISSUED")
        {
            dispatchTrigger.RequestDispatch();
        }

        return OperationResult<EmailVerificationRequestAcceptedResponse>.Success(new EmailVerificationRequestAcceptedResponse(requestId, now));
    }
}
