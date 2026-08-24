using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Infrastructure;
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
        services.AddScoped<IDevelopmentIdentityReferenceLookup, EfDevelopmentIdentityReferenceLookup>();
        services.AddScoped<IAuthenticatedIdentityReferenceLookup, EfAuthenticatedIdentityReferenceLookup>();
        services.AddSingleton<IIdentityPasswordHasher, FrameworkPasswordHasher>();
        services.AddSingleton<IIdentityTokenIssuer, JwtIdentityTokenIssuer>();
        services.AddSingleton<IRefreshTokenProtector, HmacRefreshTokenProtector>();
        services.AddSingleton<IIdentityRequestFingerprinter, HmacIdentityRequestFingerprinter>();
        services.AddSingleton<IIdentitySessionPolicy, ConfiguredIdentitySessionPolicy>();
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<Application.RegisterAccount.Handler>();
        services.AddScoped<Application.SignIn.Handler>();
        services.AddScoped<Application.RefreshSession.Handler>();
        services.AddScoped<Application.GetCurrentSession.Handler>();
        services.AddScoped<Application.SignOut.Handler>();
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
