using UnicoreCRM.Platform.Email.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Application.Common;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Email;

/// <summary>Identity owns OTP content and failures; the shared transport owns SMTP and MIME.</summary>
internal sealed class GmailSmtpIdentityEmailSender(ISmtpEmailTransport transport) : IIdentityEmailSender
{
    public void EnsureConfigured()
    {
        if (!transport.IsConfigured) throw new IdentityEmailSenderUnavailableException();
    }

    public async Task SendEmailVerificationCodeAsync(IdentityEmailVerificationMessage message, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var result = await transport.SendAsync(Compose(message), cancellationToken);
        if (result.Accepted) return;
        cancellationToken.ThrowIfCancellationRequested();
        throw new IdentityEmailDeliveryFailedException(result.ErrorCode switch
        {
            "SMTP_AUTH_FAILED" => IdentityEmailDeliveryFailure.AuthenticationFailed,
            "SMTP_RECIPIENT_INVALID" or "SMTP_RECIPIENT_REJECTED" or "SMTP_SENDER_REJECTED" => IdentityEmailDeliveryFailure.RecipientRejected,
            "SMTP_TIMEOUT" => IdentityEmailDeliveryFailure.Timeout,
            "SMTP_PROTOCOL_ERROR" => IdentityEmailDeliveryFailure.ProtocolError,
            "SMTP_COMMAND_FAILED" => IdentityEmailDeliveryFailure.CommandFailed,
            "SMTP_PROVIDER_UNAVAILABLE" => IdentityEmailDeliveryFailure.ProviderUnavailable,
            "SMTP_CONNECT_FAILED" or "SMTP_TLS_FAILED" => IdentityEmailDeliveryFailure.ConnectFailed,
            _ => IdentityEmailDeliveryFailure.Unknown
        });
    }

    private sealed class EmailContent
    {
        public string TextBody { get; init; } = "";
        public string HtmlBody { get; init; } = "";
    }

    private static SmtpEmailMessage Compose(IdentityEmailVerificationMessage message)
    {
        var expiry = message.ExpiresAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'");
        var body = new EmailContent
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
        return new(message.Email, message.DisplayName, $"{message.Code} is your UnicoreCRM verification code",
            body.TextBody, body.HtmlBody);
    }
}
