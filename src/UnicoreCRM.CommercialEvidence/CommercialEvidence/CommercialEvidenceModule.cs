using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Application;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Contracts;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Infrastructure.Persistence;

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence;

internal static class CommercialEvidenceModule
{
    internal static IServiceCollection AddCommercialEvidenceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<CommercialEvidenceDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "commercial_evidence")));
        services.AddScoped<ICommercialEvidencePersistence, EfCommercialEvidencePersistence>();
        services.AddScoped<IOrderCompletedPurchaseEvidenceAppender, OrderCompletedPurchaseEvidenceAppender>();
        services.AddScoped<IEffectivePurchaseEvidenceReader, EffectivePurchaseEvidenceReader>();
        services.TryAddSingleton<IPurchaseEvidenceIdGenerator, OpaquePurchaseEvidenceIdGenerator>();
        services.TryAddSingleton<ICommercialEvidencePolicyVersionProvider, CommercialEvidencePolicyVersionProvider>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddDevelopmentSchemaMigration(
            "commercial_evidence",
            (provider, cancellationToken) => provider
                .GetRequiredService<CommercialEvidenceDbContext>()
                .Database
                .MigrateAsync(cancellationToken));
        return services;
    }
}
