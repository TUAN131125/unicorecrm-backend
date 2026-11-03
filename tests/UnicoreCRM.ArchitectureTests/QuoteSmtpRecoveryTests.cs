using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnicoreCRM.Sales.Quotes.Application.Common;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

/// <summary>Real HTTP/SQL persistence with an explicitly simulated external provider.</summary>
public sealed class QuoteSmtpRecoveryTests
{
    public sealed class IsolatedFactAttribute : FactAttribute
    {
        public IsolatedFactAttribute()
        {
            if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("QUOTE_DELIVERY_TEST_WORKSPACE")))
                Skip="Requires the fresh isolated database and official fixtures from verify-quote-shared-smtp.ps1.";
        }
    }

    [IsolatedFact]
    public async Task Unknown_outcome_replays_without_resending_and_blocks_new_key()
    {
        var sender=new Provider { Outcome=new("UNKNOWN","GMAIL_DELIVERY_TIMEOUT") };
        await using var host=new Host(sender);
        using var client=await Login(host);
        var quote=await Issue(client);
        var key="unknown-"+Guid.NewGuid().ToString("N");
        var first=await Send(client,quote,key);
        Assert.Equal(HttpStatusCode.OK,first.StatusCode);
        Assert.Equal("UNKNOWN",(await Read(first))["deliveryStatus"]!.GetValue<string>());
        var replay=await Send(client,quote,key);
        Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        Assert.Equal("UNKNOWN",(await Read(replay))["deliveryStatus"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict,(await Send(client,quote,key+"-new")).StatusCode);
        Assert.Equal(1,sender.Calls);
        var current=await client.GetFromJsonAsync<JsonObject>("/quotes/"+quote.Id);
        Assert.Equal("DRAFT",current!["status"]!.GetValue<string>());
        Assert.Null(current["deliveryHistory"]);
    }

    [IsolatedFact]
    public async Task Legacy_ambiguous_failure_is_not_retried_or_rewritten()
    {
        var provider=new Provider {Outcome=new("FAILED","GMAIL_DELIVERY_TIMEOUT")};
        await using var host=new Host(provider);using var client=await Login(host);var q=await Issue(client);
        var key="legacy-"+Guid.NewGuid().ToString("N");
        var receipt=await Read(await Send(client,q,key));
        Assert.Equal("UNKNOWN",receipt["deliveryStatus"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict,(await Send(client,q,key+"-new")).StatusCode);
        Assert.Equal(1,provider.Calls);
        await using var connection=new Microsoft.Data.SqlClient.SqlConnection(Required("ConnectionStrings__UnicoreCRM"));
        await connection.OpenAsync();await using var command=connection.CreateCommand();
        command.CommandText="SELECT DeliveryStatus FROM quotes.AcceptanceInvitations WHERE InvitationId=@id";
        command.Parameters.AddWithValue("@id",receipt["invitationId"]!.GetValue<string>());
        Assert.Equal("FAILED",await command.ExecuteScalarAsync());
    }

    [IsolatedFact]
    public async Task Concurrent_requests_observe_committed_claim_without_locking_smtp()
    {
        var sender=new Provider { Hold=true };
        await using var host=new Host(sender);
        using var client=await Login(host);
        var quote=await Issue(client);var key="concurrent-"+Guid.NewGuid().ToString("N");
        var inFlight=Send(client,quote,key);
        await sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            // Both requests can return while the provider is held: no aggregate lock spans SMTP.
            var replay=await Send(client,quote,key).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("SENDING",(await Read(replay))["deliveryStatus"]!.GetValue<string>());
            Assert.Equal(HttpStatusCode.Conflict,(await Send(client,quote,key+"-new").WaitAsync(TimeSpan.FromSeconds(5))).StatusCode);
        }
        finally {sender.Release.TrySetResult();}
        Assert.Equal("SENT",(await Read(await inFlight))["deliveryStatus"]!.GetValue<string>());
        Assert.Equal(1,sender.Calls);
        var current=await client.GetFromJsonAsync<JsonObject>("/quotes/"+quote.Id);
        Assert.Equal("SENT",current!["status"]!.GetValue<string>());
        Assert.Equal("PROVIDER_CONFIRMED_SENT",current["deliveryHistory"]![0]!["evidenceType"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK,(await Send(client,quote,key)).StatusCode);
        Assert.Equal(1,sender.Calls);
    }

    [IsolatedFact]
    public async Task Host_restart_cannot_resend_unknown_intent()
    {
        var key="restart-"+Guid.NewGuid().ToString("N");Fixture quote;
        var firstProvider=new Provider { Outcome=new("UNKNOWN","GMAIL_DELIVERY_OUTCOME_UNKNOWN") };
        await using(var firstHost=new Host(firstProvider))
        {
            using var firstClient=await Login(firstHost);quote=await Issue(firstClient);
            Assert.Equal(HttpStatusCode.OK,(await Send(firstClient,quote,key)).StatusCode);
        }
        var secondProvider=new Provider();
        await using var restarted=new Host(secondProvider);
        using var client=await Login(restarted);
        Assert.Equal("UNKNOWN",(await Read(await Send(client,quote,key)))["deliveryStatus"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict,(await Send(client,quote,key+"-new")).StatusCode);
        Assert.Equal(0,secondProvider.Calls);
    }

    [IsolatedFact]
    public async Task Lost_SQL_acknowledgment_leaves_claim_unresolved_without_duplicate_send()
    {
        var trigger="SmtpAckFault_"+Guid.NewGuid().ToString("N");
        var sender=new Provider { BeforeOutcome=()=>Sql($"CREATE TRIGGER quotes.{trigger} ON quotes.AcceptanceInvitations AFTER UPDATE AS BEGIN THROW 51038,'Isolated SMTP outcome persistence fault',1; END") };
        await using var host=new Host(sender);
        using var client=await Login(host);var q=await Issue(client);var key="lost-ack-"+Guid.NewGuid().ToString("N");
        // Install the SQL fault only after the claim committed and the simulated provider ran.
        try {Assert.Equal(HttpStatusCode.InternalServerError,(await Send(client,q,key)).StatusCode);}
        finally {await Sql($"DROP TRIGGER quotes.{trigger}");}
        Assert.Equal("SENDING",(await Read(await Send(client,q,key)))["deliveryStatus"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.Conflict,(await Send(client,q,key+"-new")).StatusCode);
        Assert.Equal(1,sender.Calls);
        var current=await client.GetFromJsonAsync<JsonObject>("/quotes/"+q.Id);
        Assert.Equal("DRAFT",current!["status"]!.GetValue<string>());
        Assert.Null(current["deliveryHistory"]);
    }

    private sealed class Host(Provider sender) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var connection=new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(Required("ConnectionStrings__UnicoreCRM"));
            if(!System.Text.RegularExpressions.Regex.IsMatch(connection.InitialCatalog,"^UnicoreCRM_OrderConfirmationTest_[A-Za-z0-9_]+$"))
                throw new InvalidOperationException("Only the isolated Quote fixture database is allowed.");
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services=>{
                services.RemoveAll<IQuoteGmailSender>();services.AddSingleton<IQuoteGmailSender>(sender);
            });
        }
    }
    private sealed class Provider : IQuoteGmailSender
    {
        private int calls;
        public int Calls=>calls;
        public bool Hold {get;init;}
        public Func<Task>? BeforeOutcome {get;init;}
        public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public QuoteGmailResult Outcome {get;init;}=new("SENT",null);
        public string? ConfigurationError(string workspace)=>null;
        public async Task<QuoteGmailResult> SendAsync(string workspace,string recipient,string subject,string body,byte[] pdf,string fileName,string messageId,CancellationToken ct)
        {
            Interlocked.Increment(ref calls);Started.TrySetResult();
            if(Hold)await Release.Task.WaitAsync(ct);
            if(BeforeOutcome is not null)await BeforeOutcome();
            return Outcome;
        }
    }
    private sealed record Fixture(string Id,string Document,long Version);
    private static string Required(string key)=>Environment.GetEnvironmentVariable(key)??throw new InvalidOperationException("Missing isolated fixture setting: "+key);
    private static async Task Sql(string text)
    {
        await using var connection=new Microsoft.Data.SqlClient.SqlConnection(Required("ConnectionStrings__UnicoreCRM"));
        await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=text;
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<HttpClient> Login(Host host)
    {
        var client=host.CreateClient();
        var request=new HttpRequestMessage(HttpMethod.Post,"/auth/sessions") {
            Content=JsonContent.Create(new {email=Required("QUOTE_DELIVERY_TEST_EMAIL"),password=Required("QUOTE_DELIVERY_TEST_PASSWORD")})};
        request.Headers.Add("X-Request-Id",Guid.NewGuid().ToString("N"));
        request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString("N"));
        var response=await client.SendAsync(request);response.EnsureSuccessStatusCode();
        var session=await Read(response);
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",session["accessToken"]!.GetValue<string>());
        client.DefaultRequestHeaders.Add("X-Workspace-Id",Required("QUOTE_DELIVERY_TEST_WORKSPACE"));
        client.DefaultRequestHeaders.Add("X-Request-Id",Guid.NewGuid().ToString("N"));
        return client;
    }
    private static async Task<Fixture> Issue(HttpClient client)
    {
        var draft=new {
            buyerRef=new {type="CONTACT",id=Required("QUOTE_DELIVERY_TEST_BUYER")},sourcePath="DIRECT_SALE",title="Isolated SMTP recovery",
            currency="VND",recipientEmail="customer@example.test",validUntil=DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
            lineItems=new[]{new {productNameSnapshot="Recovery service",productTypeSnapshot="SERVICE",quantity="1",unitPrice=new {amount="100000",currency="VND"},discountRate="0",taxMode="EXCLUSIVE",taxRate="0"}},
            paymentAgreement=new {kind="FULL_PAYMENT",currency="VND",lines=new[]{new {sequence=1,label="Full",purpose="FULL",amountRule=new {type="REMAINDER"},dueRule=new {type="EVENT_RELATIVE",@event="ORDER_CONFIRMED",offsetDays=0,dayBasis="CALENDAR"},allowedMethodCodes=new[]{"BANK_TRANSFER"},fulfillmentGate="NONE"}}}
        };
        using var create=Command("/quotes",draft,Guid.NewGuid().ToString("N"));
        var created=await client.SendAsync(create);created.EnsureSuccessStatusCode();
        var q=(await Read(created))["result"]!["quote"]!;
        var id=q["id"]!.GetValue<string>();var version=q["resourceVersion"]!.GetValue<long>();
        using var issue=Command("/quotes/"+id+"/documents/issue",new {},Guid.NewGuid().ToString("N"),version);
        var issued=await client.SendAsync(issue);issued.EnsureSuccessStatusCode();
        var result=(await Read(issued))["result"]!;
        return new(id,result["document"]!["documentId"]!.GetValue<string>(),result["quote"]!["resourceVersion"]!.GetValue<long>());
    }
    private static Task<HttpResponseMessage> Send(HttpClient client,Fixture q,string key)=>
        client.SendAsync(Command("/quotes/"+q.Id+"/acceptance-invitations",new {documentId=q.Document,sendGmail=true},key,q.Version));
    private static HttpRequestMessage Command(string path,object body,string key,long? version=null)
    {
        var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};
        request.Headers.Add("Idempotency-Key",key);
        if(version.HasValue)request.Headers.TryAddWithoutValidation("If-Match","\""+version.Value+"\"");
        return request;
    }
    private static async Task<JsonObject> Read(HttpResponseMessage response)=>
        (await response.Content.ReadFromJsonAsync<JsonObject>())??throw new InvalidOperationException("Missing JSON response.");
}
