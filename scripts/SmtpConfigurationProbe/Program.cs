using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Platform;
using UnicoreCRM.Platform.Email.Contracts;

// This diagnostic authenticates once and sends NO email. Never print provider exceptions or config values.
if(args.Length!=1){Console.WriteLine("Specify the ApiHost configuration directory. No email is sent by this diagnostic.");return 2;}
var hostPath=Path.GetFullPath(args[0]);
IConfigurationRoot config;
try {config=new ConfigurationBuilder().SetBasePath(hostPath)
    .AddJsonFile("appsettings.json",optional:false)
    .AddJsonFile("appsettings.Development.json",optional:true)
    .AddJsonFile("appsettings.Development.Local.json",optional:true)
    .AddJsonFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft/UserSecrets/unicorecrm-apihost-development/secrets.json"),optional:true)
    .AddEnvironmentVariables().Build();}
catch(Exception){Console.WriteLine("{\"configured\":false,\"failure\":\"CONFIGURATION_UNAVAILABLE\",\"emailMessagesSent\":0}");return 1;}
using var services=new ServiceCollection().AddPlatformModule(config).BuildServiceProvider();
var transport=services.GetRequiredService<ISmtpEmailTransport>();
var section=config.GetSection("IdentityAuth:EmailVerification:Sender");
var ready=transport.IsConfigured;var authenticated=false;string? failure=null;
if(ready)
{
    using var client=new SmtpClient{Timeout=30000};
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try
    {
        await client.ConnectAsync(section["Host"]??"",section.GetValue<int>("Port"),
            section.GetValue<bool>("UseStartTls")?SecureSocketOptions.StartTls:SecureSocketOptions.SslOnConnect,timeout.Token);
        await client.AuthenticateAsync(section["Username"]??"",section["AppPassword"]??"",timeout.Token);
        authenticated=true;
    }
    catch(Exception error)
    {
        failure=error switch {
            AuthenticationException=>"SMTP_AUTH_FAILED",SslHandshakeException=>"SMTP_TLS_FAILED",
            OperationCanceledException=>"SMTP_TIMEOUT",_=>"SMTP_CONNECTION_OR_PROTOCOL_FAILED"};
    }
}
else failure="SMTP_NOT_CONFIGURED";
string? Provider(string key)=>config.Providers.Reverse().FirstOrDefault(p=>p.TryGet(key,out _))?.GetType().Name;
Console.WriteLine(JsonSerializer.Serialize(new {
    configured=ready,authenticated,failure,emailMessagesSent=0,
    credentialProvider=Provider("IdentityAuth:EmailVerification:Sender:AppPassword"),
    source="IdentityAuth:EmailVerification:Sender",realGmailDelivery="GMAIL_REAL_DELIVERY_NOT_RUN"
},new JsonSerializerOptions{WriteIndented=true}));
return authenticated?0:1;
