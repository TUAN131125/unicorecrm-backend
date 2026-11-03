using Microsoft.Extensions.Configuration;
using UnicoreCRM.Platform.Email.Contracts;
using UnicoreCRM.Sales.Quotes.Infrastructure.Documents;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteSharedSmtpSenderTests
{
    [Fact]
    public async Task Html_includes_clickable_customer_link_only_when_present_in_server_content()
    {
        var transport=new Capture();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["QuoteGmail:Workspaces:allowed:Enabled"]="true"
        }).Build();
        var sender=new QuoteGmailSender(config,transport);
        var link="https://example.test/#/quote-acceptance/"+new string('A',64);
        await sender.SendAsync("allowed","buyer@example.test","Quote","Customer acceptance: "+link,[],"quote.pdf","id",default);
        Assert.Contains("href=\""+link+"\"",transport.Message!.HtmlBody);
        await sender.SendAsync("allowed","buyer@example.test","Quote","Issued PDF only",[],"quote.pdf","pdf-only",default);
        Assert.DoesNotContain("<a ",transport.Message!.HtmlBody);
    }

    [Fact]
    public async Task Workspace_allowlist_is_not_inferred_from_acceptance_or_other_workspace()
    {
        var transport=new Capture();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["QuoteGmail:Workspaces:allowed:Enabled"]="true",
            ["QuoteSalesAcceptance:Workspaces:foreign:Enabled"]="true"
        }).Build();
        var sender=new QuoteGmailSender(config,transport);
        var result=await sender.SendAsync("foreign","buyer@example.test","quote","link",[],"quote.pdf","id",default);
        Assert.Equal("GMAIL_WORKSPACE_NOT_ALLOWED",result.ErrorCode);
        Assert.Equal(0,transport.Calls);
    }

    [Theory]
    [InlineData(true,false,null,"SENT")]
    [InlineData(false,true,"SMTP_TIMEOUT","UNKNOWN")]
    [InlineData(false,false,"SMTP_TIMEOUT","FAILED")]
    [InlineData(false,false,"SMTP_AUTH_FAILED","FAILED")]
    [InlineData(false,false,"SMTP_TLS_FAILED","FAILED")]
    public async Task Provider_outcomes_keep_certainty_without_workspace_credentials(bool accepted,bool unknown,string? error,string status)
    {
        var transport=new Capture { Result=new(accepted,unknown,error) };
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["QuoteGmail:Workspaces:allowed:Enabled"]="true"
        }).Build();
        var sender=new QuoteGmailSender(config,transport);
        var pdf=new byte[]{37,80,68,70};
        var result=await sender.SendAsync("allowed","buyer@example.test","quote","acceptance link",pdf,"quote.pdf","invitation",default);
        Assert.Equal(status,result.Status);
        if(!unknown && error=="SMTP_TIMEOUT")Assert.Equal("GMAIL_CONNECTION_TIMEOUT",result.ErrorCode);
        Assert.Equal(1,transport.Calls);
        Assert.NotNull(transport.Message);
        Assert.Equal("buyer@example.test",transport.Message.Recipient);
        Assert.True(transport.Message.UseAuthenticatedSender);
        Assert.Equal("invitation@unicorecrm.invalid",transport.Message.MessageId);
        Assert.Equal(pdf,transport.Message.Attachment?.Content);
        Assert.Contains("acceptance link",transport.Message.TextBody);
    }

    private sealed class Capture : ISmtpEmailTransport
    {
        public bool IsConfigured=>true;
        public int Calls { get; private set; }
        public SmtpEmailMessage? Message { get; private set; }
        public SmtpEmailResult Result { get; set; }=new(true,false,null);
        public Task<SmtpEmailResult> SendAsync(SmtpEmailMessage message,CancellationToken cancellationToken)
        { Calls++;Message=message;return Task.FromResult(Result); }
    }
}
