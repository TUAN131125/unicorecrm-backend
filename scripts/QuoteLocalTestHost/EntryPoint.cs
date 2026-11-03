using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UnicoreCRM.Platform.Email.Contracts;

namespace UnicoreCRM.QuoteLocalTestHost;

internal static class EntryPoint
{
    private const string Workspace="ws_f2332a12d56a421d8a722bd2f65c5efb";
    internal static async Task<int> Main(string[] args)
    {
        if(args.Length!=3 || !int.TryParse(args[2],out var port) || port is <1024 or >65535)return 2;
        var settings=Path.GetFullPath(args[0]);var evidence=Path.GetFullPath(args[1]);Directory.CreateDirectory(evidence);
        var connection=Environment.GetEnvironmentVariable("ConnectionStrings__UnicoreCRM");
        if(string.IsNullOrWhiteSpace(connection))return 2;
        var db=new SqlConnectionStringBuilder(connection);
        if(!System.Text.RegularExpressions.Regex.IsMatch(db.InitialCatalog,"^UnicoreCRM_QuoteGmailTest_[a-f0-9]{32}$"))return 2;
        var configuration=new ConfigurationBuilder().AddJsonFile(settings).Build();
        if(!Uri.TryCreate(configuration["QuoteSalesAcceptance:PublicBaseUrl"],UriKind.Absolute,out var frontend)
            || !frontend.IsLoopback || frontend.Scheme!="http")return 2;
        await using var host=new LocalHost(settings,evidence,connection);
        host.UseKestrel(options=>options.Listen(IPAddress.Loopback,port));
        host.StartServer();
        var addresses=host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        var address=addresses?.SingleOrDefault()??throw new InvalidOperationException("Test host address unavailable.");
        if(!Uri.TryCreate(address,UriKind.Absolute,out var api) || !api.IsLoopback)throw new InvalidOperationException("Non-loopback binding refused.");
        await File.WriteAllTextAsync(Path.Combine(evidence,"host.json"),JsonSerializer.Serialize(new {
            apiUrl=address,frontendUrl=frontend.GetLeftPart(UriPartial.Authority),database=db.InitialCatalog,
            workspaceId=Workspace,pid=Environment.ProcessId,removedHostedServices=host.Removed,
            realSendApprovalPresent=File.Exists(Path.Combine(evidence,"live-send-approval.json"))
        },new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("ISOLATED_QUOTE_TEST_HOST_READY");
        var stopped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stopped.TrySetResult();};
        await stopped.Task;return 0;
    }

    private sealed class LocalHost(string settings,string evidence,string connection):WebApplicationFactory<global::Program>
    {
        internal List<string> Removed {get;}=[];
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(Directory.GetCurrentDirectory());
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_,config)=>config.AddJsonFile(settings)
                .AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:UnicoreCRM",connection}}));
            builder.ConfigureServices(services=>{
                // Database copies must never dispatch copied OTP, Telegram, webhook or AI work.
                var allowed=new HashSet<string>(StringComparer.Ordinal) {
                    "UnicoreCRM.Workflows.Atomic.Application.AcceptedQuoteOrder.RecoveryService",
                    "UnicoreCRM.Workflows.Atomic.Application.AcceptedQuoteCustomer.RecoveryService",
                    "UnicoreCRM.Workflows.Atomic.Application.AcceptedQuoteOrder.DraftPlanRecoveryService"
                };
                foreach(var descriptor in services.Where(d=>d.ServiceType==typeof(IHostedService)).ToArray())
                {
                    var name=descriptor.ImplementationType?.FullName;
                    if(name=="UnicoreCRM.Workflows.Atomic.Application.AcceptedQuoteCustomer.RecoveryService"
                        && Environment.GetEnvironmentVariable("UNICORE_CUSTOMER_TEST_PAUSE_RECOVERY")=="true")
                    { Removed.Add(name);services.Remove(descriptor);continue; }
                    if(name?.StartsWith("Microsoft.",StringComparison.Ordinal)==true || name is not null && allowed.Contains(name))continue;
                    Removed.Add(name??"non-framework hosted-service factory");services.Remove(descriptor);
                }
                var original=services.Single(d=>d.ServiceType==typeof(ISmtpEmailTransport));services.Remove(original);
                services.AddSingleton<ISmtpEmailTransport>(provider=>new FencedTransport(
                    (ISmtpEmailTransport)(original.ImplementationInstance??original.ImplementationFactory?.Invoke(provider)
                        ??ActivatorUtilities.CreateInstance(provider,original.ImplementationType!)),evidence,connection));
            });
        }
    }

    private sealed record Approval(string WorkspaceId,string QuoteId,string Recipient,string PdfSha256);
    private sealed class FencedTransport(ISmtpEmailTransport original,string evidence,string connection):ISmtpEmailTransport
    {
        public bool IsConfigured=>original.IsConfigured;
        public async Task<SmtpEmailResult> SendAsync(SmtpEmailMessage message,CancellationToken ct)
        {
            var approvalPath=Path.Combine(evidence,"live-send-approval.json");
            if(!message.UseAuthenticatedSender || message.Attachment is null || !File.Exists(approvalPath))
                return new(false,false,"SMTP_RECIPIENT_INVALID");
            Approval? approval;
            try{approval=JsonSerializer.Deserialize<Approval>(await File.ReadAllTextAsync(approvalPath,ct),new JsonSerializerOptions(JsonSerializerDefaults.Web));}
            catch(Exception){return new(false,false,"SMTP_RECIPIENT_INVALID");}
            if(approval is null || approval.WorkspaceId!=Workspace || !string.Equals(approval.Recipient,message.Recipient,StringComparison.OrdinalIgnoreCase)
                || !string.Equals(approval.PdfSha256,Convert.ToHexString(SHA256.HashData(message.Attachment.Content)),StringComparison.OrdinalIgnoreCase)
                || message.MessageId is null || !message.MessageId.EndsWith("@unicorecrm.invalid",StringComparison.Ordinal))
                return new(false,false,"SMTP_RECIPIENT_INVALID");
            var invitation=message.MessageId[..message.MessageId.IndexOf('@')];
            await using var sql=new SqlConnection(connection);await sql.OpenAsync(ct);await using var command=sql.CreateCommand();
            command.CommandText="SELECT COUNT(*) FROM quotes.AcceptanceInvitations WHERE WorkspaceId=@workspace AND QuoteId=@quote AND InvitationId=@invitation AND RecipientEmail=@recipient";
            command.Parameters.AddWithValue("@workspace",Workspace);command.Parameters.AddWithValue("@quote",approval.QuoteId);
            command.Parameters.AddWithValue("@invitation",invitation);command.Parameters.AddWithValue("@recipient",approval.Recipient);
            if(Convert.ToInt32(await command.ExecuteScalarAsync(ct))!=1)return new(false,false,"SMTP_RECIPIENT_INVALID");
            var fence=Path.Combine(evidence,"live-email-attempt.json");
            try
            {
                await using var stream=new FileStream(fence,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough);
                await JsonSerializer.SerializeAsync(stream,new {workspaceId=Workspace,quoteId=approval.QuoteId,invitationId=invitation,state="SMTP_ATTEMPT_RESERVED",at=DateTimeOffset.UtcNow},cancellationToken:ct);
                await stream.FlushAsync(ct);stream.Flush(true);
            }
            catch(IOException){return new(false,false,"SMTP_PROVIDER_UNAVAILABLE");}
            var result=await original.SendAsync(message,ct);
            // Existence of the reservation blocks every further send, including after process restart.
            await File.WriteAllTextAsync(fence,JsonSerializer.Serialize(new {workspaceId=Workspace,quoteId=approval.QuoteId,invitationId=invitation,result.Accepted,result.Unknown,result.ErrorCode,emailAttempts=1}),CancellationToken.None);
            return result;
        }
    }
}
