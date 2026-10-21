using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace UnicoreCRM.Communications.Infrastructure.Telegram;

// Deployment assertions are independent of account verification and activation. No fake mode in configuration.
internal sealed record TelegramProviderAdmission(bool Allowed, bool Synthetic)
{
    internal static TelegramProviderAdmission FromConfiguration(IConfiguration configuration, IHostEnvironment? environment)
    {
        var section = configuration.GetSection("Communications:Telegram:LiveAdmission");
        var approvedEnvironment = section["ApprovedEnvironment"];
        var allowed = environment is not null && !string.IsNullOrWhiteSpace(approvedEnvironment)
            && approvedEnvironment == environment.EnvironmentName
            && section["ProviderAccessApproved"] == "true" && section["PrivacyApproved"] == "true"
            && section["DeploymentCustodyApproved"] == "true";
        return new(allowed, false);
    }
}
