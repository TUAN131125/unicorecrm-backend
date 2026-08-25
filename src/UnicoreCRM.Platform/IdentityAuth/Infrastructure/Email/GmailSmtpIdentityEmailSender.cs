using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using UnicoreCRM.Platform.IdentityAuth.Application.Common;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Email;

/// <summary>
/// SMTP delivery through Gmail, using the account's Username plus a Google App Password.
///
/// This is the only type in the solution that touches an SMTP or MIME type. It sits behind
/// <see cref="IIdentityEmailSender"/>, so no MailKit or MimeKit type appears in IdentityAuth's
/// Domain, Application or Contracts layer, and no other module can reach it.
///
/// The transport is always encrypted: STARTTLS on the submission port, or implicit TLS when
/// STARTTLS is switched off. There is deliberately no plaintext fallback.
///
/// Nothing here logs. Credentials and the verification code exist only as local values for the
/// duration of one send, and no provider-authored text ever leaves this type: a failed send is
/// classified into one of IdentityAuth's own bounded delivery failures and the provider exception is
/// discarded, so SMTP dialogue - which echoes the recipient, the headers and this product's
/// code-bearing Subject line - can reach neither a log nor the persisted delivery evidence.
/// </summary>
internal sealed class GmailSmtpIdentityEmailSender(IOptions<IdentityAuthOptions> options) : IIdentityEmailSender
{
    private EmailSenderOptions Sender => options.Value.EmailVerification.Sender;

    public void EnsureConfigured()
    {
        var sender = Sender;
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(sender.Host))
        {
            missing.Add("Host");
        }

        if (sender.Port is < 1 or > 65535)
        {
            missing.Add("Port");
        }

        if (string.IsNullOrWhiteSpace(sender.Username))
        {
            missing.Add("Username");
        }

        if (string.IsNullOrWhiteSpace(sender.AppPassword))
        {
            missing.Add("AppPassword");
        }

        if (!TryParseAddress(sender.FromAddress, sender.FromName, out _))
        {
            missing.Add("FromAddress");
        }

        if (sender.TimeoutSeconds is < 1 or > 300)
        {
            missing.Add("TimeoutSeconds");
        }

        if (missing.Count != 0)
        {
            // Names the settings, never their values.
            throw new IdentityEmailSenderUnavailableException(
                $"The GmailSmtp email sender is not usable: {string.Join(", ", missing)} missing or invalid.");
        }
    }

    public async Task SendEmailVerificationCodeAsync(IdentityEmailVerificationMessage message, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var sender = Sender;
        if (!TryParseAddress(sender.FromAddress, sender.FromName, out var from))
        {
            throw new IdentityEmailSenderUnavailableException("The GmailSmtp email sender is not usable: FromAddress missing or invalid.");
        }

        if (!TryParseAddress(message.Email, message.DisplayName, out var recipient))
        {
            // A stored recipient that no longer parses can never be delivered; failing it as a
            // delivery error lets the outbox retire the message instead of retrying forever.
            throw new IdentityEmailDeliveryFailedException(IdentityEmailDeliveryFailure.RecipientRejected);
        }

        var mail = Compose(from!, recipient!, message);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(sender.TimeoutSeconds));
        using var client = new SmtpClient { Timeout = sender.TimeoutSeconds * 1000 };
        try
        {
            var socketOptions = sender.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
            await client.ConnectAsync(sender.Host, sender.Port, socketOptions, timeout.Token);
            await client.AuthenticateAsync(sender.Username, sender.AppPassword, timeout.Token);
            await client.SendAsync(mail, timeout.Token);
            await client.DisconnectAsync(true, timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new IdentityEmailDeliveryFailedException(IdentityEmailDeliveryFailure.Timeout);
        }
        catch (Exception exception)
        {
            throw new IdentityEmailDeliveryFailedException(Classify(exception));
        }
    }

    private static MimeMessage Compose(MailboxAddress from, MailboxAddress recipient, IdentityEmailVerificationMessage message)
    {
        var expiry = message.ExpiresAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'");
        var mail = new MimeMessage();
        mail.From.Add(from);
        mail.To.Add(recipient);
        mail.Subject = $"{message.Code} is your UnicoreCRM verification code";
        var body = new BodyBuilder
        {
            TextBody =
                $"""
                UnicoreCRM

                Your email verification code is {message.Code}

                Enter this six-digit code to finish verifying your UnicoreCRM account.
                The code expires at {expiry} and can be used only once.

                If you did not create a UnicoreCRM account you can ignore this message.
                """,
            HtmlBody =
                $"""
                <div style="font-family:Segoe UI,Helvetica,Arial,sans-serif;max-width:520px;margin:0 auto;padding:32px 24px;color:#0f172a">
                  <div style="font-size:13px;font-weight:700;letter-spacing:.14em;text-transform:uppercase;color:#4f46e5">UnicoreCRM</div>
                  <h1 style="font-size:20px;font-weight:600;margin:20px 0 8px">Verify your email address</h1>
                  <p style="font-size:15px;line-height:1.6;color:#475569;margin:0 0 24px">
                    Enter this six-digit code to finish verifying your UnicoreCRM account.
                  </p>
                  <div style="font-size:34px;font-weight:700;letter-spacing:.32em;padding:18px 8px;text-align:center;background:#eef2ff;border-radius:14px;color:#312e81">
                    {message.Code}
                  </div>
                  <p style="font-size:14px;line-height:1.6;color:#475569;margin:24px 0 0">
                    The code expires at <strong>{expiry}</strong> and can be used only once.
                  </p>
                  <p style="font-size:13px;line-height:1.6;color:#94a3b8;margin:24px 0 0">
                    If you did not create a UnicoreCRM account you can ignore this message.
                  </p>
                </div>
                """
        };
        mail.Body = body.ToMessageBody();
        mail.Headers.Add("X-Entity-Ref-ID", Guid.NewGuid().ToString("N"));
        return mail;
    }

    private static bool TryParseAddress(string? address, string? displayName, out MailboxAddress? mailbox)
    {
        mailbox = null;
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        if (!MailboxAddress.TryParse(address.Trim(), out var parsed))
        {
            return false;
        }

        mailbox = string.IsNullOrWhiteSpace(displayName)
            ? parsed
            : new MailboxAddress(displayName.Trim(), parsed.Address);
        return true;
    }

    /// <summary>
    /// Maps a provider fault onto one of IdentityAuth's own bounded delivery classifications, and
    /// discards the original exception entirely.
    ///
    /// The provider's text is never carried forward, not even redacted. SMTP error text quotes server
    /// dialogue, and that dialogue routinely echoes the envelope: the recipient address, the headers,
    /// and for this product a Subject line that contains the verification code itself. Redacting the
    /// values this host happens to know - a username, an app password - is a denylist, and a denylist
    /// cannot cover text the provider composes. Only the exception's <em>type</em> is inspected, and
    /// only the classification leaves this method.
    /// </summary>
    private static IdentityEmailDeliveryFailure Classify(Exception exception) => exception switch
    {
        AuthenticationException => IdentityEmailDeliveryFailure.AuthenticationFailed,
        SmtpCommandException command => command.ErrorCode switch
        {
            SmtpErrorCode.RecipientNotAccepted or SmtpErrorCode.SenderNotAccepted => IdentityEmailDeliveryFailure.RecipientRejected,
            _ => IdentityEmailDeliveryFailure.CommandFailed
        },
        SmtpProtocolException => IdentityEmailDeliveryFailure.ProtocolError,
        ServiceNotConnectedException or ServiceNotAuthenticatedException => IdentityEmailDeliveryFailure.ProviderUnavailable,
        SslHandshakeException or System.Net.Sockets.SocketException or IOException => IdentityEmailDeliveryFailure.ConnectFailed,
        TimeoutException or OperationCanceledException => IdentityEmailDeliveryFailure.Timeout,
        _ => IdentityEmailDeliveryFailure.Unknown
    };
}
