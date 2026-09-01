using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UnicoreCRM.BuildingBlocks;
using Microsoft.IdentityModel.Tokens;
using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Infrastructure;
using UnicoreCRM.Platform.IdentityAuth.Infrastructure.Email;
using UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence;
using UnicoreCRM.Platform.IdentityAuth.Infrastructure.Security;

namespace UnicoreCRM.Platform.IdentityAuth;

internal static class IdentityAuthModule
{
    internal static IServiceCollection AddIdentityAuthModule(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(IdentityAuthOptions.SectionName).Get<IdentityAuthOptions>() ?? new IdentityAuthOptions();
        services.AddOptions<IdentityAuthOptions>()
            .Bind(configuration.GetSection(IdentityAuthOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.Session.AbsoluteDays >= options.Session.IdleDays, "Absolute session lifetime must be at least the idle lifetime.")
            .ValidateOnStart();

        var connectionString = configuration.GetConnectionString("UnicoreCRM");
        services.AddDbContext<IdentityAuthDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "iam")));
        services.AddScoped<IIdentityAuthPersistence, EfIdentityAuthPersistence>();
        services.AddDevelopmentSchemaMigration(
            "identity-auth",
            (provider, cancellationToken) => provider.GetRequiredService<IdentityAuthDbContext>().Database.MigrateAsync(cancellationToken));
        services.AddScoped<IDevelopmentIdentityReferenceLookup, EfDevelopmentIdentityReferenceLookup>();
        services.AddScoped<IAuthenticatedIdentityReferenceLookup, EfAuthenticatedIdentityReferenceLookup>();
        services.AddScoped<IIdentityAccessDirectoryProfileSource, EfIdentityAccessDirectoryProfileSource>();
        services.AddSingleton<IIdentityPasswordHasher, FrameworkPasswordHasher>();
        services.AddSingleton<IIdentityTokenIssuer, JwtIdentityTokenIssuer>();
        services.AddSingleton<IRefreshTokenProtector, HmacRefreshTokenProtector>();
        services.AddSingleton<IIdentityRequestFingerprinter, HmacIdentityRequestFingerprinter>();
        services.AddSingleton<IIdentitySessionPolicy, ConfiguredIdentitySessionPolicy>();
        services.AddSingleton<IIdentityVerificationCodeProtector, HmacIdentityVerificationCodeProtector>();
        services.AddSingleton<IIdentityEmailVerificationPolicy, ConfiguredIdentityEmailVerificationPolicy>();
        services.AddSingleton<IIdentityEmailPayloadProtector, AesGcmIdentityEmailPayloadProtector>();
        // Email delivery fails closed by default. GmailSmtp is the real provider and is available to
        // any environment; the Development console sender is resolved only when the running host is
        // Development and asks for it by name, so no deployed host can fall back to a fake sender.
        // Every unrecognised kind resolves the unavailable sender.
        services.AddSingleton<IIdentityEmailSender>(provider => settings.EmailVerification.Sender.Kind switch
        {
            "GmailSmtp" => ActivatorUtilities.CreateInstance<GmailSmtpIdentityEmailSender>(provider),
            "DevelopmentLog" when provider.GetRequiredService<IHostEnvironment>().IsDevelopment() =>
                ActivatorUtilities.CreateInstance<DevelopmentLoggingIdentityEmailSender>(provider),
            // The deliberately hostile sender used to prove that provider error text cannot reach the
            // outbox or the log. Gated exactly like the console sender.
            "DevelopmentFailing" when provider.GetRequiredService<IHostEnvironment>().IsDevelopment() =>
                ActivatorUtilities.CreateInstance<SimulatedFailingIdentityEmailSender>(provider),
            _ => ActivatorUtilities.CreateInstance<UnavailableIdentityEmailSender>(provider)
        });
        services.AddSingleton<IdentityEmailOutboxSignal>();
        services.AddSingleton<IIdentityEmailDispatchTrigger>(provider => provider.GetRequiredService<IdentityEmailOutboxSignal>());
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<EmailVerificationChallengeIssuer>();
        services.AddScoped<Application.RegisterAccount.Handler>();
        services.AddScoped<Application.RequestEmailVerification.Handler>();
        services.AddScoped<Application.VerifyEmail.Handler>();
        services.AddScoped<Application.SignIn.Handler>();
        services.AddScoped<Application.RefreshSession.Handler>();
        services.AddScoped<Application.GetCurrentSession.Handler>();
        services.AddScoped<Application.SignOut.Handler>();
        services.AddHostedService<IdentityEmailOutboxDispatcher>();
        services.AddHostedService<DevelopmentIdentityBootstrap>();

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Jwt.SigningKey)),
                    ValidateIssuer = true,
                    ValidIssuer = settings.Jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Jwt.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name"
                };
                options.Events = JwtSessionValidationEvents.Create();
            });
        services.AddAuthorization();
        return services;
    }
}
