using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using UnicoreCRM.Platform.Email.Contracts;

namespace UnicoreCRM.Platform.Email.Infrastructure;

internal sealed class SharedSmtpOptions
{
    public string Kind { get; set; } = "Unavailable";
    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = "";
    public string AppPassword { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "UnicoreCRM";
    public int TimeoutSeconds { get; set; } = 30;
}

internal sealed class SharedSmtpEmailTransport(IOptions<SharedSmtpOptions> options) : ISmtpEmailTransport
{
    public bool IsConfigured => Valid(options.Value);

    internal static bool Valid(SharedSmtpOptions sender) => sender.Kind == "GmailSmtp"
        && !string.IsNullOrWhiteSpace(sender.Host) && sender.Port is >= 1 and <= 65535
        && sender.TimeoutSeconds is >= 1 and <= 300 && !string.IsNullOrWhiteSpace(sender.AppPassword)
        && !string.IsNullOrWhiteSpace(sender.Username)
        && MailboxAddress.TryParse(sender.FromAddress, out _);

    public async Task<SmtpEmailResult> SendAsync(SmtpEmailMessage message, CancellationToken cancellationToken)
    {
        var sender = options.Value;
        if (!Valid(sender)) return new(false, false, "SMTP_NOT_CONFIGURED");
        if (message.UseAuthenticatedSender && !MailboxAddress.TryParse(sender.Username, out _))
            return new(false, false, "SMTP_NOT_CONFIGURED");
        if (!MailboxAddress.TryParse(message.Recipient, out var recipient)) return new(false, false, "SMTP_RECIPIENT_INVALID");
        using var mail = Compose(sender, recipient, message);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(sender.TimeoutSeconds));
        using var client = new SmtpClient { Timeout = sender.TimeoutSeconds * 1000 };
        var sendStarted = false;
        try
        {
            await client.ConnectAsync(sender.Host, sender.Port,
                sender.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect, timeout.Token);
            await client.AuthenticateAsync(sender.Username, sender.AppPassword, timeout.Token);
            sendStarted = true;
            await client.SendAsync(mail, timeout.Token);
            // Disposing the connection cannot change an accepted message into a failed delivery.
            return new(true, false, null);
        }
        catch (Exception error) when (error is OperationCanceledException or AuthenticationException
            or SslHandshakeException or SmtpCommandException or SmtpProtocolException
            or ServiceNotConnectedException or ServiceNotAuthenticatedException
            or IOException or System.Net.Sockets.SocketException or TimeoutException)
        {
            return Classify(error, sendStarted);
        }
        catch (Exception)
        {
            // No provider-authored text or inner exception may leave the transport.
            return new(false, sendStarted, "SMTP_UNKNOWN_FAILURE");
        }
    }

    internal static MimeMessage Compose(SharedSmtpOptions sender, MailboxAddress recipient, SmtpEmailMessage message)
    {
        var mail = new MimeMessage();
        mail.From.Add(new MailboxAddress(sender.FromName,
            MailboxAddress.Parse(message.UseAuthenticatedSender ? sender.Username : sender.FromAddress).Address));
        mail.To.Add(new MailboxAddress(message.RecipientName, recipient.Address));
        mail.Subject = message.Subject;
        if (message.MessageId is not null) mail.MessageId = message.MessageId;
        var body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody };
        if (message.Attachment is { } attachment)
            body.Attachments.Add(attachment.FileName, attachment.Content, new ContentType("application", "pdf"));
        mail.Body = body.ToMessageBody();
        return mail;
    }

    internal static SmtpEmailResult Classify(Exception error, bool sendStarted)
    {
        var code = error switch
        {
            AuthenticationException => "SMTP_AUTH_FAILED",
            SslHandshakeException => "SMTP_TLS_FAILED",
            OperationCanceledException or TimeoutException => "SMTP_TIMEOUT",
            SmtpCommandException { ErrorCode: SmtpErrorCode.RecipientNotAccepted } => "SMTP_RECIPIENT_REJECTED",
            SmtpCommandException { ErrorCode: SmtpErrorCode.SenderNotAccepted } => "SMTP_SENDER_REJECTED",
            SmtpCommandException => "SMTP_COMMAND_FAILED",
            SmtpProtocolException => "SMTP_PROTOCOL_ERROR",
            ServiceNotConnectedException or ServiceNotAuthenticatedException => "SMTP_PROVIDER_UNAVAILABLE",
            _ => "SMTP_CONNECT_FAILED"
        };
        // Explicit negative SMTP replies are definitive; loss of a reply during DATA is ambiguous.
        return new(false, sendStarted && error is not SmtpCommandException, code);
    }
}
