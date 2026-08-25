using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.Common;

/// <summary>
/// The single place that issues an email-verification challenge, shared by registration and by an
/// explicit verification request. Issuing always supersedes every outstanding challenge for the
/// account first, so at most one code is ever usable and a resend durably invalidates the previous
/// one.
///
/// The caller owns the surrounding transaction. This type saves the challenge and then dispatches
/// the message, so a sender that fails closed prevents the caller from committing: an account is
/// never left holding a code that was never dispatched. A real remote provider should later move
/// dispatch behind an IdentityAuth-owned outbox rather than widening this boundary.
/// </summary>
internal sealed class EmailVerificationChallengeIssuer(
    IIdentityAuthPersistence persistence,
    IIdentityVerificationCodeProtector codes,
    IIdentityEmailVerificationPolicy policy,
    IIdentityEmailSender emailSender)
{
    internal async Task<IdentityEmailVerificationChallenge> IssueAsync(
        IdentityAccount account,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var outstanding in await persistence.ListOutstandingEmailVerificationChallengesAsync(account.AccountId, cancellationToken))
        {
            outstanding.Supersede(now);
        }

        var code = codes.Create();
        var challenge = new IdentityEmailVerificationChallenge(
            account.AccountId,
            codes.Hash(account.AccountId, code),
            now,
            now.Add(policy.CodeLifetime),
            now.Add(policy.ResendInterval),
            policy.MaxAttempts);
        persistence.AddEmailVerificationChallenge(challenge);
        persistence.AddSecurityEvent(new IdentitySecurityEvent("IDENTITY_EMAIL_VERIFICATION_ISSUED", account.AccountId, correlationId, now));
        await persistence.SaveChangesAsync(cancellationToken);
        await emailSender.SendEmailVerificationCodeAsync(
            new IdentityEmailVerificationMessage(account.Email, account.DisplayName, code, challenge.ExpiresAt),
            cancellationToken);
        return challenge;
    }
}
