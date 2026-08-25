using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnicoreCRM.Platform.IdentityAuth.Application.Common;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Email;

/// <summary>
/// The fail-closed sender. It is the default in every environment and the only sender any
/// non-Development host can resolve until a real provider is implemented and configured. It never
/// pretends a message was dispatched, so the caller's transaction cannot commit an account or a
/// challenge whose code nobody received.
/// </summary>
internal sealed class UnavailableIdentityEmailSender(ILogger<UnavailableIdentityEmailSender> logger) : IIdentityEmailSender
{
    public void EnsureConfigured() => throw new IdentityEmailSenderUnavailableException();

    public Task SendEmailVerificationCodeAsync(IdentityEmailVerificationMessage message, CancellationToken cancellationToken)
    {
        // The recipient address is deliberately not logged: this path is reachable from an
        // anonymous endpoint and must not turn the log into an address collector.
        logger.LogError("Identity email delivery is not configured; the email verification message was not dispatched.");
        throw new IdentityEmailSenderUnavailableException();
    }
}

/// <summary>
/// Development-only sender. It writes the verification code to the backend console so a local
/// developer can complete the flow without an email provider. It is registered only when the host
/// environment is Development <em>and</em> the sender kind is explicitly configured, so it can
/// never be reached by a deployed host.
/// </summary>
internal sealed class DevelopmentLoggingIdentityEmailSender(
    ILogger<DevelopmentLoggingIdentityEmailSender> logger,
    IOptions<IdentityAuthOptions> options) : IIdentityEmailSender
{
    public void EnsureConfigured()
    {
        // A console writer is always usable.
    }

    public async Task SendEmailVerificationCodeAsync(IdentityEmailVerificationMessage message, CancellationToken cancellationToken)
    {
        // A real send takes seconds; a console write takes none, which hides every question about
        // how long a delivery attempt may legitimately stay in flight. The configured pause lets a
        // verification harness hold an attempt open on purpose and observe the claim protecting it.
        // It honours the caller's token, so a claim deadline still cuts the send short.
        var pause = Math.Clamp(options.Value.EmailVerification.Sender.SimulatedSendDelayMilliseconds, 0, 120_000);
        if (pause > 0)
        {
            await Task.Delay(pause, cancellationToken);
        }

        // Written only once the send is complete, so a harness watching this log sees the code
        // exactly when a real provider would have accepted the message.
        logger.LogWarning(
            "DEVELOPMENT EMAIL VERIFICATION | to={Email} | code={Code} | expiresAt={ExpiresAt:O}",
            message.Email,
            message.Code,
            message.ExpiresAt);
    }
}

/// <summary>
/// Development-only sender that always fails, the way a hostile provider would.
///
/// It exists for one reason: to prove that provider-authored error text cannot reach IdentityAuth's
/// durable state or its logs. A real SMTP server composes its own error strings, and those strings
/// routinely quote the envelope back - the recipient address, the headers, and for this product a
/// Subject line that carries the verification code itself. This sender fabricates exactly that worst
/// case, embedding the recipient, the full subject and the live six-digit code in the exception it
/// throws, and it throws a plain exception rather than a classified delivery failure so the
/// dispatcher's own last-resort handling is what gets tested.
///
/// The fabricated text is also written to <see cref="EmailSenderOptions.SimulatedFailureTranscriptPath"/>,
/// which stands in for the provider's own transcript. That file is how a verification harness learns
/// the exact strings it must then prove are absent from <c>iam.EmailOutboxMessages.LastError</c> and
/// from the host log, so the assertion rests on the real values rather than on trusting this type.
///
/// Like the console sender it is registered only when the running host environment is Development and
/// the kind is explicitly asked for, so no deployed host can reach it.
/// </summary>
internal sealed class SimulatedFailingIdentityEmailSender(IOptions<IdentityAuthOptions> options) : IIdentityEmailSender
{
    public void EnsureConfigured()
    {
        // The simulated provider is always reachable; only its sends fail.
    }

    public Task SendEmailVerificationCodeAsync(IdentityEmailVerificationMessage message, CancellationToken cancellationToken)
    {
        var subject = $"{message.Code} is your UnicoreCRM verification code";
        var providerText =
            $"550 5.7.1 <{message.Email}>: rejected; message \"{subject}\" for {message.DisplayName} " +
            $"contained code {message.Code}; auth user {options.Value.EmailVerification.Sender.Username}";
        var transcript = options.Value.EmailVerification.Sender.SimulatedFailureTranscriptPath;
        if (!string.IsNullOrWhiteSpace(transcript))
        {
            File.AppendAllText(transcript, providerText + Environment.NewLine);
        }

        throw new InvalidOperationException(providerText);
    }
}
