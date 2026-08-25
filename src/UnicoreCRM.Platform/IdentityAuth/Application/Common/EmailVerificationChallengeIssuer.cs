using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.Common;

/// <summary>
/// The single place that issues an email-verification challenge, shared by registration and by an
/// explicit verification request. Issuing always supersedes every outstanding challenge for the
/// account first, so at most one code is ever usable and a resend durably invalidates the previous
/// one.
///
/// Superseding a challenge revokes its code, so the undelivered message carrying that code is
/// cancelled in the same transaction and its payload is dropped. Without that, an outbox row could
/// outlive the credential it carries and the system would email a code that is already invalid - the
/// holder would enter it, fail, and spend an attempt of the challenge that actually is active.
///
/// A code that is <em>already being delivered</em> cannot be revoked that way, because the send may
/// already have reached the provider. When the account's current message is in flight, issuance backs
/// off with <see cref="IdentityEmailDeliveryInFlightException"/> instead of creating the forbidden
/// state - old code invalid, new code active, old email still arriving. The claim is bounded by the
/// delivery lease, so the caller only has to ask again.
///
/// The caller owns the surrounding transaction, and this type performs no network I/O inside it. It
/// checks first that the host could deliver mail at all - a misconfigured or absent sender throws
/// here, before anything is written, so no account is created that could never be activated - and
/// then stages the challenge and one outbox message in the same transaction. The dispatcher delivers
/// after the commit, so a transient provider failure is a delivery retry rather than a lost account.
/// </summary>
internal sealed class EmailVerificationChallengeIssuer(
    IIdentityAuthPersistence persistence,
    IIdentityVerificationCodeProtector codes,
    IIdentityEmailPayloadProtector payloads,
    IIdentityEmailVerificationPolicy policy,
    IIdentityEmailSender emailSender,
    TimeProvider timeProvider)
{
    internal async Task<IdentityEmailVerificationChallenge> IssueAsync(
        IdentityAccount account,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Fail closed before writing anything: a host that cannot deliver mail must not create the
        // state that depends on delivery.
        emailSender.EnsureConfigured();

        var outstanding = await persistence.ListOutstandingEmailVerificationChallengesAsync(account.AccountId, cancellationToken);
        await RevokeAsync(outstanding, now, cancellationToken);

        var code = codes.Create();
        var challenge = new IdentityEmailVerificationChallenge(
            account.AccountId,
            codes.Hash(account.AccountId, code),
            now,
            now.Add(policy.CodeLifetime),
            now.Add(policy.ResendInterval),
            policy.MaxAttempts);
        persistence.AddEmailVerificationChallenge(challenge);
        persistence.AddEmailOutboxMessage(new IdentityEmailOutboxMessage(
            account.AccountId,
            challenge.ChallengeId,
            account.Email,
            account.DisplayName,
            payloads.Protect(challenge.ChallengeId, code),
            challenge.ExpiresAt,
            now,
            policy.DeliveryMaxAttempts));
        persistence.AddSecurityEvent(new IdentitySecurityEvent("IDENTITY_EMAIL_VERIFICATION_ISSUED", account.AccountId, correlationId, now));
        await persistence.SaveChangesAsync(cancellationToken);
        return challenge;
    }

    /// <summary>
    /// Supersedes the given challenges and retires the messages that still carry their codes.
    ///
    /// The in-flight test is read inside the caller's serialisable transaction, and a delivery claim
    /// is committed by its own transaction before any network call. The two therefore serialise: this
    /// transaction either sees the claim and refuses to revoke, or commits the cancellation first and
    /// the message is no longer claimable. There is no interleaving in which the code is revoked and
    /// the email still reaches the provider.
    /// </summary>
    private async Task RevokeAsync(
        IReadOnlyList<IdentityEmailVerificationChallenge> outstanding,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (outstanding.Count == 0)
        {
            return;
        }

        var challengeIds = outstanding.Select(x => x.ChallengeId).ToArray();
        var undelivered = await persistence.ListUndeliveredEmailOutboxMessagesAsync(challengeIds, cancellationToken);
        // The clock is read again here rather than reusing the caller's issuance instant, so a lease
        // is never judged expired because of time spent earlier in the same request.
        var leaseCheckedAt = timeProvider.GetUtcNow();
        if (undelivered.Any(message => message.IsDeliveryInFlight(leaseCheckedAt)))
        {
            throw new IdentityEmailDeliveryInFlightException();
        }

        foreach (var challenge in outstanding)
        {
            challenge.Supersede(now);
        }

        foreach (var message in undelivered)
        {
            message.Cancel(now, EmailOutboxReasons.ChallengeSuperseded);
        }
    }
}
