using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Platform.IdentityAuth;
using UnicoreCRM.Platform.Workspace;
using UnicoreCRM.Platform.AccessControl;

namespace UnicoreCRM.Platform;

public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.AddOptions<Email.Infrastructure.SharedSmtpOptions>()
            .Bind(configuration.GetSection("IdentityAuth:EmailVerification:Sender"));
        services.AddSingleton<Email.Contracts.ISmtpEmailTransport, Email.Infrastructure.SharedSmtpEmailTransport>();
        services.AddIdentityAuthModule(configuration);
        services.AddWorkspaceModule(configuration);
        services.AddAccessControlModule(configuration);

        return services;
    }
}
