using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.RegisterAccount;

internal sealed class Handler(
    IIdentityAuthPersistence persistence,
    IIdentityPasswordHasher passwordHasher,
    EmailVerificationChallengeIssuer issuer,
    IIdentityRequestFingerprinter fingerprinter,
    TimeProvider timeProvider)
{
    internal async Task<OperationResult<UserAccountDocument>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(command);
        if (validation.Count != 0)
            return OperationResult<UserAccountDocument>.Failure(IdentityErrors.Validation(validation));

        var email = command.Email.Trim();
        var fingerprint = fingerprinter.Create(email.ToUpperInvariant(), command.Password, command.DisplayName);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var prior = await persistence.FindIdempotencyAsync("registerAccount", command.Metadata.IdempotencyKey, cancellationToken);
        if (prior is not null)
        {
            if (!string.Equals(prior.Fingerprint, fingerprint, StringComparison.Ordinal))
                return OperationResult<UserAccountDocument>.Failure(IdentityErrors.IdempotencyReused());
            var replay = await persistence.FindAccountByIdAsync(prior.ResourceId, cancellationToken);
            return replay is null
                ? OperationResult<UserAccountDocument>.Failure(new OperationError("INTERNAL_ERROR", 500, "Internal server error"))
                : OperationResult<UserAccountDocument>.Success(IdentityProjection.Account(replay));
        }

        if (await persistence.FindAccountByEmailAsync(email.ToUpperInvariant(), cancellationToken) is not null)
            return OperationResult<UserAccountDocument>.Failure(IdentityErrors.DuplicateEmail());

        var now = timeProvider.GetUtcNow();
        var account = new IdentityAccount(email, command.DisplayName.Trim(), now);
        persistence.AddAccount(account);
        persistence.AddCredential(new IdentityCredential(account.AccountId, passwordHasher.Hash(account, command.Password), now));
        persistence.AddIdempotency(new IdentityIdempotencyRecord("registerAccount", command.Metadata.IdempotencyKey, fingerprint, account.AccountId, now));
        persistence.AddAudit(new IdentityAuditRecord("registerAccount", "SUCCEEDED", account.AccountId, command.Metadata.CorrelationId, now));
        persistence.AddSecurityEvent(new IdentitySecurityEvent("IDENTITY_ACCOUNT_REGISTERED", account.AccountId, command.Metadata.CorrelationId, now));
        await persistence.SaveChangesAsync(cancellationToken);
        // The account is created awaiting verification, so registration is only complete once the
        // first challenge is persisted and dispatched. A sender that fails closed leaves the
        // transaction uncommitted, so no account is stranded without a way to reach Active.
        try
        {
            await issuer.IssueAsync(account, command.Metadata.CorrelationId, now, cancellationToken);
        }
        catch (IdentityEmailSenderUnavailableException)
        {
            return OperationResult<UserAccountDocument>.Failure(IdentityErrors.EmailDeliveryUnavailable());
        }

        await transaction.CommitAsync(cancellationToken);
        return OperationResult<UserAccountDocument>.Success(IdentityProjection.Account(account));
    }
}
