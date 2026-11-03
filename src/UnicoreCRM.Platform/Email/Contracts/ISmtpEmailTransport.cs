namespace UnicoreCRM.Platform.Email.Contracts;

/// <summary>Server-only transport. Callers retain authorization, content and durable delivery ownership.</summary>
public interface ISmtpEmailTransport
{
    bool IsConfigured { get; }
    Task<SmtpEmailResult> SendAsync(SmtpEmailMessage message, CancellationToken cancellationToken);
}

public sealed record SmtpEmailMessage(string Recipient, string RecipientName, string Subject, string TextBody,
    string HtmlBody, string? MessageId = null, SmtpEmailAttachment? Attachment = null, bool UseAuthenticatedSender = false);
public sealed record SmtpEmailAttachment(string FileName, byte[] Content);

/// <summary>Accepted means SMTP acceptance only. Unknown must never trigger an automatic resend.</summary>
public sealed record SmtpEmailResult(bool Accepted, bool Unknown, string? ErrorCode);
