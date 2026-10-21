using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Communications.Infrastructure.Persistence;
using UnicoreCRM.Communications.Application;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Communications.Infrastructure.Telegram;

namespace UnicoreCRM.Communications;

public static class CommunicationsModule
{
    public static IServiceCollection AddCommunicationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CommunicationsDbContext>(options => options.UseSqlServer(
            configuration.GetConnectionString("UnicoreCRM"),
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "communications")));
        services.AddDevelopmentSchemaMigration("communications", (provider, ct) =>
            provider.GetRequiredService<CommunicationsDbContext>().Database.MigrateAsync(ct));
        services.AddDataProtection();
        services.AddSingleton(new TelegramPollingOptions { Enabled = configuration.GetValue<bool>("Communications:Telegram:Enabled") });
        services.AddSingleton<ITelegramCredentialCustody>(_ => PersistentTelegramCustody.Create(configuration));
        services.AddSingleton(provider => TelegramProviderAdmission.FromConfiguration(configuration,
            provider.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>()));
        services.AddScoped<TelegramCredentialVault>();
        services.AddScoped<ITelegramCredentials>(provider => provider.GetRequiredService<TelegramCredentialVault>());
        services.AddScoped<TelegramConnectionManagement>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<LocalCommunicationsStore>();
        services.AddScoped<TelegramInboundCoordinator>();
        services.AddHttpClient<TelegramApiClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.telegram.org/");
            client.Timeout = TimeSpan.FromSeconds(20);
        }).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
            ActivityHeadersPropagator = null, ConnectTimeout = TimeSpan.FromSeconds(5)
        });
        services.AddHostedService<TelegramPollingWorker>();
        services.AddScoped<CommunicationsReader>();
        services.AddScoped<ConversationClaimer>();
        services.AddScoped<ConversationTransitioner>();
        services.AddScoped<ConversationLinker>();
        services.AddScoped<ChannelManagement>();
        services.AddScoped<IRecordAccessFactProvider, ChannelAccessFacts>();
        services.AddScoped<IRecordAccessFactProvider, ConversationAccessFacts>();
        services.AddScoped<IRecordAccessFactProvider, QueueAccessFacts>();
        return services;
    }
}
