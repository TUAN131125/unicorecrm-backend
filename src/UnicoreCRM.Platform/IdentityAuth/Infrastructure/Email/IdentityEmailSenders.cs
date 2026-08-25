using Microsoft.Extensions.Logging;
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
internal sealed class DevelopmentLoggingIdentityEmailSender(ILogger<DevelopmentLoggingIdentityEmailSender> logger) : IIdentityEmailSender
{
    public Task SendEmailVerificationCodeAsync(IdentityEmailVerificationMessage message, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "DEVELOPMENT EMAIL VERIFICATION | to={Email} | code={Code} | expiresAt={ExpiresAt:O}",
            message.Email,
            message.Code,
            message.ExpiresAt);
        return Task.CompletedTask;
    }
}
