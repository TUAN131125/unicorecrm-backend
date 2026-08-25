using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.Common;

/// <summary>
/// The IdentityAuth-owned outbound email boundary. IdentityAuth defines the message it needs and
/// never depends on a provider SDK, transport or template engine.
///
/// The two failure modes are deliberately distinct:
///
/// <list type="bullet">
/// <item><see cref="EnsureConfigured"/> answers "could this host ever deliver mail?" without any
/// network call. It is checked on the request path, before anything is persisted, so a host with a
/// missing or unusable sender configuration fails closed and creates no account that could never be
/// activated.</item>
/// <item><see cref="SendEmailVerificationCodeAsync"/> performs the remote call. It runs only from
/// the outbox dispatcher, after the issuing transaction has committed, and its failures are
/// transient delivery failures that the outbox retries.</item>
/// </list>
///
/// There is deliberately no fake or no-op production sender.
/// </summary>
internal interface IIdentityEmailSender
{
    /// <summary>Validates the sender's own configuration. Throws <see cref="IdentityEmailSenderUnavailableException"/> when this host cannot deliver mail at all.</summary>
    void EnsureConfigured();

    Task SendEmailVerificationCodeAsync(IdentityEmailVerificationMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// The plaintext verification code lives inside this message only while a delivery attempt is in
/// flight. It is never persisted in the clear, audited or returned on the wire.
/// </summary>
internal sealed record IdentityEmailVerificationMessage(
    string Email,
    string DisplayName,
    string Code,
    DateTimeOffset ExpiresAt);

/// <summary>
/// This host cannot deliver mail at all: no sender is configured, or the configured sender is
/// missing required settings. It fails the request closed and is never retried.
/// </summary>
internal sealed class IdentityEmailSenderUnavailableException : Exception
{
    internal IdentityEmailSenderUnavailableException()
        : this("No identity email sender is configured for this environment.") { }

    internal IdentityEmailSenderUnavailableException(string message)
        : base(message) { }
}

/// <summary>
/// One delivery attempt failed. The message stays in the outbox and is retried.
///
/// It carries a <see cref="Failure"/> classification and nothing else. Provider error text quotes
/// server dialogue, and SMTP dialogue routinely echoes the recipient address and the message subject
/// - and this product's subject line contains the verification code itself. Redacting known secrets
/// out of such a string is a denylist and cannot be complete, so no provider-authored text is carried
/// at all: the sender classifies the fault and discards the original. The exception's own
/// <see cref="Exception.Message"/> is the bounded code, so even an accidental log of it is safe.
/// </summary>
internal sealed class IdentityEmailDeliveryFailedException : Exception
{
    internal IdentityEmailDeliveryFailedException(IdentityEmailDeliveryFailure failure)
        : base(IdentityEmailDeliveryFailureCodes.For(failure)) => Failure = failure;

    internal IdentityEmailDeliveryFailure Failure { get; }

    /// <summary>The bounded, application-owned code that may be persisted and logged for this fault.</summary>
    internal string ReasonCode => IdentityEmailDeliveryFailureCodes.For(Failure);
}

/// <summary>
/// The bounded set of delivery faults IdentityAuth recognises. A sender maps whatever its provider
/// threw onto exactly one of these; anything it cannot place becomes <see cref="Unknown"/>.
/// </summary>
internal enum IdentityEmailDeliveryFailure
{
    Unknown = 0,
    AuthenticationFailed,
    ConnectFailed,
    Timeout,
    ProtocolError,
    CommandFailed,
    RecipientRejected,
    ProviderUnavailable
}

internal static class IdentityEmailDeliveryFailureCodes
{
    internal static string For(IdentityEmailDeliveryFailure failure) => failure switch
    {
        IdentityEmailDeliveryFailure.AuthenticationFailed => EmailOutboxReasons.SmtpAuthFailed,
        IdentityEmailDeliveryFailure.ConnectFailed => EmailOutboxReasons.SmtpConnectFailed,
        IdentityEmailDeliveryFailure.Timeout => EmailOutboxReasons.SmtpTimeout,
        IdentityEmailDeliveryFailure.ProtocolError => EmailOutboxReasons.SmtpProtocolError,
        IdentityEmailDeliveryFailure.CommandFailed => EmailOutboxReasons.SmtpCommandFailed,
        IdentityEmailDeliveryFailure.RecipientRejected => EmailOutboxReasons.SmtpRecipientRejected,
        IdentityEmailDeliveryFailure.ProviderUnavailable => EmailOutboxReasons.SmtpProviderUnavailable,
        _ => EmailOutboxReasons.UnknownDeliveryFailure
    };
}

/// <summary>
/// A code for this account is claimed by a delivery attempt that has not resolved yet, so the
/// attempt could still reach the provider. Issuing a replacement now would revoke a code that is
/// already on its way, which is exactly the state the outbox must never produce, so issuance backs
/// off instead. The claim is bounded by the delivery lease, so the caller can simply ask again.
/// </summary>
internal sealed class IdentityEmailDeliveryInFlightException : Exception
{
    internal IdentityEmailDeliveryInFlightException()
        : base("A verification code for this account is currently being delivered.") { }
}

/// <summary>
/// Creates, hashes and compares six-digit verification codes. Comparison is fixed-time and the
/// caller never sees a stored code.
/// </summary>
internal interface IIdentityVerificationCodeProtector
{
    string Create();
    string Hash(string accountId, string code);
    bool Matches(string accountId, string code, string expectedHash);
}

/// <summary>
/// Reversible protection for the one value the outbox must be able to reconstruct after the issuing
/// transaction commits: the code it still has to deliver. Verification itself never uses this - it
/// compares against the one-way digest on the challenge - so a lost or rotated key costs at most an
/// undeliverable queued message.
/// </summary>
internal interface IIdentityEmailPayloadProtector
{
    string Protect(string challengeId, string code);

    bool TryUnprotect(string challengeId, string protectedPayload, out string code);
}

/// <summary>
/// Lets a committed transaction ask the outbox dispatcher to run now instead of waiting for its next
/// idle pass. It is a latency optimisation only: dropping the signal delays a message, never loses
/// it, because the idle pass finds the same durable rows.
/// </summary>
internal interface IIdentityEmailDispatchTrigger
{
    void RequestDispatch();
}

internal interface IIdentityEmailVerificationPolicy
{
    TimeSpan CodeLifetime { get; }
    TimeSpan ResendInterval { get; }
    int MaxAttempts { get; }

    /// <summary>Delivery attempts allowed for one queued message. Unrelated to <see cref="MaxAttempts"/>.</summary>
    int DeliveryMaxAttempts { get; }
}
