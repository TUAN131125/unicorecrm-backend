namespace UnicoreCRM.Platform.IdentityAuth.Domain;

/// <summary>
/// One issued email-verification challenge for exactly one account.
///
/// The challenge persists only a keyed hash of the six-digit code. The plaintext code exists in
/// memory for the duration of the issuing request and is handed to the email sender; it is never
/// persisted, logged by the application, audited or returned on the wire.
///
/// A challenge is <em>outstanding</em> until it is consumed or superseded, and it is <em>usable</em>
/// only while it is outstanding, unexpired and below its attempt ceiling. Issuing a new challenge
/// supersedes every outstanding one, so a resend durably invalidates the previous code.
/// </summary>
internal sealed class IdentityEmailVerificationChallenge
{
    private IdentityEmailVerificationChallenge() { }

    internal IdentityEmailVerificationChallenge(
        string accountId,
        string codeHash,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        DateTimeOffset resendAvailableAt,
        int maxAttempts)
    {
        ChallengeId = IdentityIds.New("evc");
        AccountId = accountId;
        CodeHash = codeHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
        ResendAvailableAt = resendAvailableAt;
        MaxAttempts = maxAttempts;
        AttemptCount = 0;
    }

    public string ChallengeId { get; private set; } = null!;
    public string AccountId { get; private set; } = null!;
    public string CodeHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset ResendAvailableAt { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }

    internal bool IsOutstanding => ConsumedAt is null && SupersededAt is null;

    internal bool HasAttemptsRemaining => AttemptCount < MaxAttempts;

    internal bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    internal bool CanResend(DateTimeOffset now) => now >= ResendAvailableAt;

    internal void RegisterFailedAttempt()
    {
        if (!IsOutstanding)
        {
            throw new InvalidOperationException("A consumed or superseded challenge cannot record an attempt.");
        }

        AttemptCount++;
    }

    internal void Consume(DateTimeOffset now)
    {
        if (!IsOutstanding)
        {
            throw new InvalidOperationException("A consumed or superseded challenge cannot be consumed again.");
        }

        ConsumedAt = now;
    }

    internal void Supersede(DateTimeOffset now)
    {
        if (!IsOutstanding)
        {
            return;
        }

        SupersededAt = now;
    }
}
