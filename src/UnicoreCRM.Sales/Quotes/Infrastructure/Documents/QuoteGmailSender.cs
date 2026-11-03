using Microsoft.Extensions.Configuration;
using UnicoreCRM.Platform.Email.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Documents;

internal sealed class QuoteGmailSender(IConfiguration configuration, ISmtpEmailTransport transport):IQuoteGmailSender
{
    public string? ConfigurationError(string workspace) =>
        !configuration.GetValue<bool>("QuoteGmail:Workspaces:"+workspace+":Enabled") ? "GMAIL_WORKSPACE_NOT_ALLOWED"
        : !transport.IsConfigured ? "GMAIL_NOT_CONFIGURED" : null;
    public async Task<QuoteGmailResult> SendAsync(string workspace,string recipient,string subject,string body,byte[] pdf,string fileName,string messageId,CancellationToken ct)
    {
        // Permission to use the system sender remains separate from acceptance/business policy.
        if (ConfigurationError(workspace) is {} configurationError) return new("FAILED", configurationError);
        var html="<p>"+System.Net.WebUtility.HtmlEncode(body).Replace("\n","<br>")+"</p>";
        var link=System.Text.RegularExpressions.Regex.Match(body,@"https?://[^\s]+/#/quote-acceptance/[A-F0-9]{64}").Value;
        if(link.Length>0)html+="<p><a href=\""+System.Net.WebUtility.HtmlEncode(link)+"\">Chấp nhận báo giá</a></p>";
        var result=await transport.SendAsync(new(recipient,"",subject,body,html,messageId+"@unicorecrm.invalid",
            new(fileName,pdf),UseAuthenticatedSender:true),ct);
        return new(result.Accepted?"SENT":result.Unknown?"UNKNOWN":"FAILED",result.ErrorCode switch
        {
            null => null,
            "SMTP_NOT_CONFIGURED" => "GMAIL_NOT_CONFIGURED",
            "SMTP_RECIPIENT_INVALID" or "SMTP_RECIPIENT_REJECTED" => "GMAIL_RECIPIENT_INVALID",
            "SMTP_AUTH_FAILED" => "GMAIL_AUTHENTICATION_FAILED",
            "SMTP_TLS_FAILED" => "GMAIL_TLS_FAILED",
            "SMTP_TIMEOUT" => result.Unknown ? "GMAIL_DELIVERY_TIMEOUT" : "GMAIL_CONNECTION_TIMEOUT",
            "SMTP_COMMAND_FAILED" or "SMTP_SENDER_REJECTED" => "GMAIL_PROVIDER_REJECTED",
            _ => result.Unknown ? "GMAIL_DELIVERY_OUTCOME_UNKNOWN" : "GMAIL_CONNECTION_FAILED"
        });
    }
}
