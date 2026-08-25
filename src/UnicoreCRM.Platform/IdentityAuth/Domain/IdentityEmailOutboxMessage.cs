namespace UnicoreCRM.Platform.IdentityAuth.Domain;

/// <summary>
/// The complete vocabulary that may ever be persisted in <see cref="IdentityEmailOutboxMessage.LastError"/>.
///
/// Nothing provider-authored reaches that column. A delivery failure is classified into one of these
/// bounded, application-owned constants before it is recorded, so a provider whose error text echoes
/// the recipient address, the message subject or the verification code itself cannot write any of it
/// into IdentityAuth's durable state or into a log line.
/// </summary>
internal static class EmailOutboxReasons
{
    internal const string SenderUnavailable = "EMAIL_SENDER_UNAVAILABLE";
    internal const string SmtpAuthFailed = "SMTP_AUTH_FAILED";
    internal const string SmtpConnectFailed = "SMTP_CONNECT_FAILED";
    internal const string SmtpTimeout = "SMTP_TIMEOUT";
    internal const string SmtpProtocolError = "SMTP_PROTOCOL_ERROR";
    internal const string SmtpCommandFailed = "SMTP_COMMAND_FAILED";
    internal const string SmtpRecipientRejected = "SMTP_RECIPIENT_REJECTED";
    internal const string SmtpProviderUnavailable = "SMTP_PROVIDER_UNAVAILABLE";
    internal const string UnknownDeliveryFailure = "UNKNOWN_DELIVERY_FAILURE";

    internal const string PayloadUnreadable = "PAYLOAD_UNREADABLE";
    internal const string CodeExpiredBeforeDelivery = "CODE_EXPIRED_BEFORE_DELIVERY";

    internal const string ChallengeSuperseded = "CHALLENGE_SUPERSEDED";
    internal const string ChallengeConsumed = "CHALLENGE_CONSUMED";
    internal const string ChallengeExpired = "CHALLENGE_EXPIRED";
    internal const string ChallengeNotDeliverable = "CHALLENGE_NOT_DELIVERABLE";
}

/// <summary>
/// One durable outbound verification email, owned by IdentityAuth.
///
/// It exists so the issuing transaction never performs remote SMTP I/O. The transaction commits the
/// account, the challenge and this message together; a dispatcher delivers afterwards. The message
/// is keyed one-to-one to its challenge, so a delivery retry can never produce a second account or a
/// second challenge - the only thing a retry can repeat is the email itself.
///
/// <see cref="ProtectedCode"/> holds the code under authenticated encryption, never in the clear,
/// and is cleared the moment the message reaches a terminal state so the ciphertext lives no longer
/// than the delivery it exists for.
///
/// A message carries a credential, so it is deliverable only for as long as its challenge is. The
/// moment the challenge is superseded, consumed or expired the message is <see cref="Cancel"/>led:
/// terminal, non-deliverable and stripped of its payload, rather than recorded as though it had been
/// sent. <see cref="LeasedUntil"/> makes an in-flight delivery attempt visible to other transactions,
/// which is what lets an issuing transaction tell "not delivered yet" from "being delivered right
/// now" and refuse to revoke a code that is already on its way to the provider.
/// </summary>
internal sealed class IdentityEmailOutboxMessage
{
    private IdentityEmailOutboxMessage() { }

    internal IdentityEmailOutboxMessage(
        string accountId,
        string challengeId,
        string recipient,
        string displayName,
        string protectedCode,
        DateTimeOffset codeExpiresAt,
        DateTimeOffset now,
        int maxAttempts)
    {
        MessageId = IdentityIds.New("eom");
        AccountId = accountId;
        ChallengeId = challengeId;
        Recipient = recipient;
        DisplayName = displayName;
        ProtectedCode = protectedCode;
        CodeExpiresAt = codeExpiresAt;
        Status = EmailOutboxStatus.Pending;
        AttemptCount = 0;
        MaxAttempts = maxAttempts;
        CreatedAt = now;
        NextAttemptAt = now;
    }

    public string MessageId { get; private set; } = null!;
    public string AccountId { get; private set; } = null!;
    public string ChallengeId { get; private set; } = null!;
    public string Recipient { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? ProtectedCode { get; private set; }
    public DateTimeOffset CodeExpiresAt { get; private set; }
    public EmailOutboxStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>
    /// When the current delivery claim lapses, or <c>null</c> when no attempt is in flight. It is the
    /// one durable signal that separates "queued, waiting for its next attempt" from "handed to the
    /// provider right now", and the claim's own transaction commits it before any network call, so
    /// every other transaction can see it.
    /// </summary>
    public DateTimeOffset? LeasedUntil { get; private set; }

    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>
    /// Optimistic concurrency token, maintained by the database on every update.
    ///
    /// A delivery attempt reads this row, then spends seconds talking to a provider before writing
    /// its outcome. Meanwhile a resend or a verification may legitimately retire the same row. The
    /// token makes that collision loud: an outcome written against a stale image fails rather than
    /// silently overwriting the newer state, which is what stops a finished send from stamping
    /// <see cref="EmailOutboxStatus.Sent"/> over a row that has since been cancelled - a claim that
    /// a revoked code was delivered.
    /// </summary>
    public byte[] RowVersion { get; private set; } = [];

    internal bool IsPending => Status == EmailOutboxStatus.Pending;

    /// <summary>
    /// True while a delivery attempt is claimed and could still reach the provider. A message in this
    /// state must not have its credential revoked underneath it: the caller waits for the attempt to
    /// resolve instead.
    /// </summary>
    internal bool IsDeliveryInFlight(DateTimeOffset now) => IsPending && LeasedUntil is { } until && now < until;

    /// <summary>
    /// Claims the message for one delivery attempt. The attempt is counted and the next attempt is
    /// pushed out by the lease, so a dispatcher that dies mid-send releases the message by expiry
    /// rather than stranding it, and two passes never send the same message concurrently. The claim
    /// commits before any network call, which is what makes <see cref="LeasedUntil"/> a reliable
    /// answer to "is a code already on its way?".
    /// </summary>
    internal void Lease(DateTimeOffset now, TimeSpan lease)
    {
        if (!IsPending)
        {
            throw new InvalidOperationException("Only a pending outbox message can be leased.");
        }

        AttemptCount++;
        LastAttemptAt = now;
        LeasedUntil = now.Add(lease);
        NextAttemptAt = now.Add(lease);
    }

    internal void MarkSent(DateTimeOffset now)
    {
        Status = EmailOutboxStatus.Sent;
        SentAt = now;
        ProtectedCode = null;
        LeasedUntil = null;
        LastError = null;
    }

    /// <summary>
    /// Records a failed attempt against a bounded, application-owned reason code from
    /// <see cref="EmailOutboxReasons"/>. The message is rescheduled while attempts remain and
    /// abandoned once the ceiling is reached. An abandoned message keeps its reason code as delivery
    /// evidence but drops its payload.
    /// </summary>
    internal void MarkFailed(DateTimeOffset now, string reasonCode, TimeSpan backoff)
    {
        LastError = reasonCode;
        LeasedUntil = null;
        if (AttemptCount >= MaxAttempts)
        {
            Status = EmailOutboxStatus.Abandoned;
            ProtectedCode = null;
            return;
        }

        NextAttemptAt = now.Add(backoff);
    }

    /// <summary>Abandons a message that can never be delivered, such as one whose payload cannot be read.</summary>
    internal void Abandon(DateTimeOffset now, string reasonCode)
    {
        Status = EmailOutboxStatus.Abandoned;
        ProtectedCode = null;
        LeasedUntil = null;
        LastError = reasonCode;
        LastAttemptAt = now;
    }

    /// <summary>
    /// Gives up this message's delivery claim without having sent anything.
    ///
    /// Only the dispatcher holding the claim may call it, and only to resolve a claim it has decided
    /// not to use - it has already established that no send will happen. The attempt it counted
    /// stands. Releasing lets the message then be retired like any unclaimed one, which is what keeps
    /// <see cref="Cancel"/>'s guard meaningful for every other caller.
    /// </summary>
    internal void ReleaseClaim() => LeasedUntil = null;

    /// <summary>
    /// Retires a message whose credential stopped being valid, because its challenge was superseded,
    /// consumed or has expired. The message becomes terminal and non-deliverable and drops its
    /// payload; it is deliberately <em>not</em> recorded as sent, because nothing was sent.
    ///
    /// Cancelling an in-flight claim is refused: that send may already have reached the provider, so
    /// the caller must let the attempt resolve rather than pretend it never happened.
    /// </summary>
    internal void Cancel(DateTimeOffset now, string reasonCode)
    {
        if (IsDeliveryInFlight(now))
        {
            throw new InvalidOperationException("An in-flight outbox message cannot be cancelled.");
        }

        if (!IsPending)
        {
            return;
        }

        Status = EmailOutboxStatus.Cancelled;
        ProtectedCode = null;
        LeasedUntil = null;
        LastError = reasonCode;
    }
}

internal enum EmailOutboxStatus
{
    Pending,
    Sent,
    Abandoned,

    /// <summary>
    /// Terminal and non-deliverable: the challenge whose code this message carried stopped being
    /// usable before that code was delivered. Deliberately distinct from <see cref="Sent"/>, because
    /// the email never left, and from <see cref="Abandoned"/>, which means delivery was attempted and
    /// failed.
    /// </summary>
    Cancelled
}
