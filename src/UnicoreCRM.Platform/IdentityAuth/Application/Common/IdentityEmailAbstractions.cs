namespace UnicoreCRM.Platform.IdentityAuth.Application.Common;

/// <summary>
/// The IdentityAuth-owned outbound email boundary. IdentityAuth defines the message it needs and
/// never depends on a provider SDK, transport or template engine.
///
/// There is deliberately no fake or no-op production implementation: a host without a configured
/// sender resolves the unavailable sender, which fails closed, so an account is never created or
/// left waiting for a code that was never dispatched.
/// </summary>
internal interface IIdentityEmailSender
{
    Task SendEmailVerificationCodeAsync(IdentityEmailVerificationMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// The plaintext verification code lives only inside this message, only for the duration of the
/// issuing request. It is never persisted, audited or returned on the wire.
/// </summary>
internal sealed record IdentityEmailVerificationMessage(
    string Email,
    string DisplayName,
    string Code,
    DateTimeOffset ExpiresAt);

internal sealed class IdentityEmailSenderUnavailableException : Exception
{
    internal IdentityEmailSenderUnavailableException()
        : base("No identity email sender is configured for this environment.") { }
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

internal interface IIdentityEmailVerificationPolicy
{
    TimeSpan CodeLifetime { get; }
    TimeSpan ResendInterval { get; }
    int MaxAttempts { get; }
}
