using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Documents;

internal sealed class QuoteGmailSender(IConfiguration configuration):IQuoteGmailSender
{
    public async Task<string?> SendAsync(string workspace,string recipient,string subject,string body,byte[] pdf,string fileName,string messageId,CancellationToken ct)
    {
        var section=configuration.GetSection("QuoteGmail:Workspaces:"+workspace);
        var username=section["Username"]; var password=section["AppPassword"]; var from=section["FromAddress"];
        if(!section.GetValue<bool>("Enabled") || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)
            || !MailboxAddress.TryParse(username,out _) || !MailboxAddress.TryParse(from,out var sender))return "GMAIL_NOT_CONFIGURED";
        if(!MailboxAddress.TryParse(recipient,out var target))return "GMAIL_RECIPIENT_INVALID";
        var message=new MimeMessage(); message.From.Add(sender);message.To.Add(target);message.Subject=subject;
        message.MessageId=messageId+"@unicorecrm.invalid";
        var link=System.Text.RegularExpressions.Regex.Match(body,@"https?://[^\s]+/#/quote-acceptance/[A-F0-9]{64}").Value;
        var builder=new BodyBuilder{TextBody=body,HtmlBody="<p>"+System.Net.WebUtility.HtmlEncode(body).Replace("\n","<br>")+"</p>"
            +(link.Length==0?"":"<p><a style=\"display:inline-block;padding:12px 20px;background:#5b21b6;color:white;text-decoration:none\" href=\""+System.Net.WebUtility.HtmlEncode(link)+"\">Chấp nhận báo giá</a></p>")};
        builder.Attachments.Add(fileName,pdf,new ContentType("application","pdf"));message.Body=builder.ToMessageBody();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var client=new SmtpClient{Timeout=30000};
        try
        {
            await client.ConnectAsync("smtp.gmail.com",587,SecureSocketOptions.StartTls,timeout.Token);
            await client.AuthenticateAsync(username,password,timeout.Token);
            // SMTP acceptance is the delivery result. A failed QUIT after this point must
            // not turn an accepted message into FAILED and encourage duplicate sending.
            await client.SendAsync(message,timeout.Token);return null;
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(OperationCanceledException){return "GMAIL_DELIVERY_TIMEOUT";}
        catch(MailKit.Security.AuthenticationException){return "GMAIL_AUTHENTICATION_FAILED";}
        catch(MailKit.Security.SslHandshakeException){return "GMAIL_TLS_FAILED";}
        catch(Exception e)when(e is SmtpCommandException or SmtpProtocolException or System.IO.IOException or System.Net.Sockets.SocketException)
        {return "GMAIL_DELIVERY_FAILED";}
    }
}
