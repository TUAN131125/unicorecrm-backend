using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.CommercialEvidence.CommercialEvidence;

namespace UnicoreCRM.CommercialEvidence;

public static class CommercialEvidenceModule
{
    public static IServiceCollection AddCommercialEvidenceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        UnicoreCRM.CommercialEvidence.CommercialEvidence.CommercialEvidenceModule.AddCommercialEvidenceModule(
            services,
            configuration);

        return services;
    }
}
