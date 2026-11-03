using Microsoft.Extensions.Options;
using MimeKit;
using UnicoreCRM.Platform.Email.Contracts;
using UnicoreCRM.Platform.Email.Infrastructure;
using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Infrastructure.Email;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class SharedSmtpTransportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lost_acknowledgment_is_unknown_only_after_send_started(bool started)
    {
        var result = SharedSmtpEmailTransport.Classify(new IOException("private provider transcript"), started);
        Assert.Equal(started, result.Unknown);
        Assert.False(result.Accepted);
        Assert.Equal("SMTP_CONNECT_FAILED", result.ErrorCode);
    }

    [Fact]
    public void Timeout_after_data_does_not_claim_definitive_failure()
    {
        Assert.True(SharedSmtpEmailTransport.Classify(new OperationCanceledException(), true).Unknown);
        Assert.False(SharedSmtpEmailTransport.Classify(new OperationCanceledException(), false).Unknown);
    }

    [Fact]
    public async Task Development_logger_cannot_be_treated_as_real_smtp()
    {
        var transport = new SharedSmtpEmailTransport(Options.Create(new SharedSmtpOptions { Kind = "DevelopmentLog" }));
        Assert.False(transport.IsConfigured);
        var result = await transport.SendAsync(new("buyer@example.test", "Buyer", "Quote", "body", "body"), default);
        Assert.Equal(new SmtpEmailResult(false, false, "SMTP_NOT_CONFIGURED"), result);
    }

    [Fact]
    public void Quote_mime_preserves_pdf_link_recipient_and_authenticated_sender()
    {
        var sender = new SharedSmtpOptions { Username = "system@example.test", FromAddress = "otp-alias@example.test" };
        var pdf = new byte[] { 37, 80, 68, 70 };
        using var message = SharedSmtpEmailTransport.Compose(sender, new MailboxAddress("Buyer", "buyer@example.test"),
            new("buyer@example.test", "Buyer", "Quote Q-1", "https://example.test/acceptance", "<a href='https://example.test/acceptance'>Accept</a>",
                "invitation@unicorecrm.invalid", new("Q-1.pdf", pdf), UseAuthenticatedSender: true));
        Assert.Equal("system@example.test", Assert.Single(message.From.Mailboxes).Address);
        Assert.Equal("buyer@example.test", Assert.Single(message.To.Mailboxes).Address);
        Assert.Equal("invitation@unicorecrm.invalid", message.MessageId);
        Assert.Contains("https://example.test/acceptance", message.TextBody);
        var attachment = Assert.IsType<MimePart>(Assert.Single(message.Attachments));
        Assert.Equal("application/pdf", attachment.ContentType.MimeType);
        Assert.Equal("Q-1.pdf", attachment.FileName);
        using var output = new MemoryStream();
        Assert.NotNull(attachment.Content);
        attachment.Content.DecodeTo(output);
        Assert.Equal(pdf, output.ToArray());
    }

    [Fact]
    public async Task Otp_keeps_its_content_and_existing_failure_boundary()
    {
        var transport = new CaptureTransport();
        var otp = new GmailSmtpIdentityEmailSender(transport);
        await otp.SendEmailVerificationCodeAsync(new("user@example.test", "User", "123456", DateTimeOffset.UtcNow.AddMinutes(10)), default);
        Assert.Contains("123456", transport.Message!.TextBody);
        Assert.Equal("123456 is your UnicoreCRM verification code", transport.Message.Subject);
        Assert.Null(transport.Message.Attachment);
        Assert.False(transport.Message.UseAuthenticatedSender);
        transport.Result = new(false, false, "SMTP_AUTH_FAILED");
        var failure = await Assert.ThrowsAsync<IdentityEmailDeliveryFailedException>(() =>
            otp.SendEmailVerificationCodeAsync(new("user@example.test", "User", "123456", DateTimeOffset.UtcNow), default));
        Assert.Equal(IdentityEmailDeliveryFailure.AuthenticationFailed, failure.Failure);
        Assert.DoesNotContain("123456", failure.Message);
    }

    private sealed class CaptureTransport : ISmtpEmailTransport
    {
        public bool IsConfigured => true;
        public SmtpEmailMessage? Message { get; private set; }
        public SmtpEmailResult Result { get; set; } = new(true, false, null);
        public Task<SmtpEmailResult> SendAsync(SmtpEmailMessage message, CancellationToken cancellationToken)
        { Message = message; return Task.FromResult(Result); }
    }
}
