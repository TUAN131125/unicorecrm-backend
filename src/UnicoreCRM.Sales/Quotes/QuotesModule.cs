using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

namespace UnicoreCRM.Sales.Quotes;

internal static class QuotesModule
{
    internal static IServiceCollection AddQuotesModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<QuotesDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "quotes")));
        services.AddScoped<IQuotesPersistence, EfQuotesPersistence>();
        services.Configure<QuoteSalesAcceptanceOptions>(configuration.GetSection("QuoteSalesAcceptance"));
        services.AddSingleton<QuoteSalesPolicyReader>();
        services.AddScoped<QuoteSalesAcceptanceService>();
        services.AddScoped<EfQuoteSalesAcceptanceStore>();
        services.AddScoped<Contracts.IQuoteOrderConfirmationBinding,Infrastructure.Persistence.QuoteOrderConfirmationBindingReader>();
        services.AddScoped<IQuoteSalesAcceptanceStore>(p=>p.GetRequiredService<EfQuoteSalesAcceptanceStore>());
        services.AddScoped<Contracts.IAcceptedQuoteOrderQueue>(p=>p.GetRequiredService<EfQuoteSalesAcceptanceStore>());
        services.AddScoped<IQuoteGmailSender,Infrastructure.Documents.QuoteGmailSender>();
        services.AddScoped<IQuoteProofPersistence, EfQuoteProofPersistence>();
        services.AddScoped<QuoteProofAccess>();
        services.AddScoped<QuoteProofCommands>();
        services.AddScoped<QuoteProofReads>();
        services.AddScoped<IQuoteIdentityPersistence, EfQuoteIdentityPersistence>();
        services.AddScoped<QuoteIdentityAccess>();
        services.AddScoped<QuoteIdentityEvaluation>();
        services.AddScoped<QuoteManualSourceWorkflow>();
        services.AddScoped<QuoteIdentityCaseService>();
        services.AddScoped<QuoteIndependentReviewService>();
        services.AddScoped<QuoteEvidenceSourceService>();
        services.Configure<QuoteIdentityOptions>(configuration.GetSection("QuoteIdentityPolicies"));
        services.AddSingleton<IQuoteIdentityPolicyReader, QuoteIdentityPolicyReader>();
        services.AddSingleton<IQuoteIdentityCommitProbe>(provider=>
        {
            if(!provider.GetRequiredService<IHostEnvironment>().IsDevelopment() || !configuration.GetValue<bool>("QuoteIdentityVerification:Enabled"))return new DevelopmentQuoteIdentityCommitProbe();
            var workspace=configuration["QuoteIdentityVerification:WorkspaceId"];
            if(!QuotePricing.EntityId(workspace))throw new InvalidOperationException("Invalid isolated identity verification Workspace.");
            return new DevelopmentQuoteIdentityCommitProbe(workspace,configuration.GetValue<bool>("QuoteIdentityVerification:FailAfterFlush"));
        });
        services.AddScoped<QuoteProofEffectiveScan>();
        services.AddScoped<QuoteProofScanReads>();
        services.Configure<QuoteProofScanOptions>(configuration.GetSection("QuoteProofScan"));
        services.AddSingleton<IQuoteProofScanPolicyReader, QuoteProofScanPolicyReader>();
        services.AddScoped<IQuoteProofScanStore, EfQuoteProofScanStore>();
        services.AddScoped<QuoteProofScanCoordinator>();
        services.AddSingleton(provider =>
        {
            try
            {
                var settings=provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<QuoteProofScanOptions>>().CurrentValue;
                return new Infrastructure.Documents.TrustedClamAvRuntimeReader(settings.RuntimeManifestPath,settings.RuntimeManifestSha256);
            }
            catch(Exception e)when(e is InvalidOperationException or Microsoft.Extensions.Options.OptionsValidationException)
            {return new Infrastructure.Documents.TrustedClamAvRuntimeReader("","");}
        });
        services.AddSingleton<Infrastructure.Documents.ITrustedClamAvRuntimeReader>(p=>p.GetRequiredService<Infrastructure.Documents.TrustedClamAvRuntimeReader>());
        services.AddSingleton<IQuoteProofRuntimeAuthority>(p=>p.GetRequiredService<Infrastructure.Documents.TrustedClamAvRuntimeReader>());
        services.AddSingleton<IQuoteProofStructuralValidator, Infrastructure.Documents.RuntimeProofStructuralValidator>();
        services.AddSingleton<Contracts.ITrustedProofScanner, Infrastructure.Documents.ClamAvProofScanner>();
        services.AddSingleton<IQuoteProofScanCommitProbe>(provider =>
        {
            if(!provider.GetRequiredService<IHostEnvironment>().IsDevelopment() || !configuration.GetValue<bool>("QuoteProofScanVerification:Enabled"))return new Infrastructure.Documents.DevelopmentProofScanCommitProbe();
            var workspace=configuration["QuoteProofScanVerification:WorkspaceId"];
            if(!QuotePricing.EntityId(workspace))throw new InvalidOperationException("Invalid isolated scan verification Workspace.");
            return new Infrastructure.Documents.DevelopmentProofScanCommitProbe(workspace,configuration.GetValue<bool>("QuoteProofScanVerification:FailAfterResultFlush"),configuration.GetValue<int>("QuoteProofScanVerification:PauseAfterClaimMilliseconds"));
        });
        services.AddHostedService<Infrastructure.Documents.QuoteProofScanWorker>();
        services.AddSingleton<IQuoteProofCommitProbe>(provider =>
        {
            if(!provider.GetRequiredService<IHostEnvironment>().IsDevelopment() || !configuration.GetValue<bool>("QuoteProofVerification:Enabled")) return new DevelopmentProofCommitProbe();
            var workspace=configuration["QuoteProofVerification:WorkspaceId"];
            if(!QuotePricing.EntityId(workspace)) throw new InvalidOperationException("Invalid proof verification Workspace.");
            return new DevelopmentProofCommitProbe(workspace,configuration.GetValue<bool>("QuoteProofVerification:FailAfterStorageFlush"));
        });
        services.AddSingleton<IQuoteProofCipher, QuoteProofCipher>();
        services.Configure<QuoteProofStorageOptions>(configuration.GetSection("QuoteProofStoragePolicies"));
        services.AddSingleton<IQuoteProofPolicyReader, QuoteProofPolicyReader>();
        services.AddScoped<IQuoteDocumentPersistence, EfQuoteDocumentPersistence>();
        services.AddScoped<QuoteDocumentCommands>();
        services.AddScoped<QuoteDocumentReads>();
        services.Configure<QuoteDocumentOptions>(configuration.GetSection("QuoteDocumentPolicies"));
        services.AddSingleton<IQuoteDocumentPolicyReader, QuoteDocumentPolicyReader>();
        services.AddSingleton<IQuotePdfRenderer, Infrastructure.Documents.WorkerQuotePdfRenderer>();
        services.AddSingleton<IQuoteDocumentCommitProbe>(provider =>
        {
            if (!provider.GetRequiredService<IHostEnvironment>().IsDevelopment() || !configuration.GetValue<bool>("QuoteDocumentVerification:Enabled"))
                return new Infrastructure.Documents.DevelopmentDocumentCommitProbe();
            var workspace = configuration["QuoteDocumentVerification:WorkspaceId"];
            if (!QuotePricing.EntityId(workspace)) throw new InvalidOperationException("Invalid document verification Workspace.");
            return new Infrastructure.Documents.DevelopmentDocumentCommitProbe(workspace, configuration.GetValue<bool>("QuoteDocumentVerification:FailAfterStorageFlush"));
        });
        services.AddDevelopmentSchemaMigration(
            "quotes",
            (provider, cancellationToken) => provider.GetRequiredService<QuotesDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<QuoteAuthorization>();
        services.AddScoped<QuoteDraftCommands>();
        services.AddSingleton(provider =>
        {
            var system = provider.GetRequiredService<TimeProvider>();
            if (!provider.GetRequiredService<IHostEnvironment>().IsDevelopment()
                || !configuration.GetValue<bool>("QuoteLifecycleVerification:Enabled")) return new QuoteLifecycleClock(system);
            var workspace = configuration["QuoteLifecycleVerification:WorkspaceId"];
            var utc = configuration["QuoteLifecycleVerification:UtcNow"];
            if (!QuotePricing.EntityId(workspace) || !DateTimeOffset.TryParseExact(utc,
                ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var at)) throw new InvalidOperationException("Invalid isolated Quote verification clock configuration.");
            return new QuoteLifecycleClock(system, workspace, at);
        });
        services.AddScoped<QuoteRevisionCommands>();
        services.AddScoped<QuoteExpirationCommands>();
        services.AddScoped<QuoteArchiveCommands>();
        services.Configure<QuoteLifecycleOptions>(configuration.GetSection("QuoteLifecyclePolicies"));
        services.AddSingleton<IQuoteLifecyclePolicyReader, ConfiguredQuoteLifecyclePolicyReader>();
        services.AddScoped<QuoteApprovalCommands>();
        services.AddScoped<QuoteDeliveryCommands>();
        services.AddScoped<QuoteRejectionCommands>();
        services.AddScoped<QuoteAcceptanceGuard>();
        services.AddScoped<QuoteVerifiedAcceptanceAssessment>();
        services.Configure<QuoteVerifiedAcceptanceOptions>(configuration.GetSection("QuoteVerifiedAcceptancePolicies"));
        services.AddSingleton<IQuoteVerifiedAcceptancePolicyReader, QuoteVerifiedAcceptancePolicyReader>();
        services.Configure<QuoteCustomerDecisionOptions>(configuration.GetSection("QuoteCustomerDecisionPolicies"));
        services.AddSingleton<IQuoteCustomerDecisionPolicyReader, ConfiguredQuoteCustomerDecisionPolicyReader>();
        services.AddScoped<QuoteCustomerResponseCommands>();
        services.AddScoped<Application.ListQuotes.Handler>();
        services.AddScoped<Application.GetQuote.Handler>();
        services.AddScoped<IRecordAccessFactProvider, Application.ProvideQuoteRecordAccessFacts.QuoteRecordAccessFactProvider>();
        return services;
    }
}
