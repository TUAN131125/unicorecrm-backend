using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Security;

internal static class JwtSessionValidationEvents
{
    internal static JwtBearerEvents Create() => new()
    {
        OnTokenValidated = async context =>
        {
            var accountId = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var sessionId = context.Principal?.FindFirst("sid")?.Value;
            if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(sessionId))
            {
                context.Fail("Required identity claims are missing.");
                return;
            }

            var persistence = context.HttpContext.RequestServices.GetRequiredService<IIdentityAuthPersistence>();
            var session = await persistence.FindSessionAsync(sessionId, context.HttpContext.RequestAborted);
            if (session is null || session.AccountId != accountId || session.Status != SessionStatus.Active || !session.CanRefresh(TimeProvider.System.GetUtcNow()))
                context.Fail("The authoritative session is inactive.");
        },
        OnChallenge = async context =>
        {
            if (context.Response.HasStarted)
                return;
            context.HandleResponse();
            var correlationId = Contracts.IdentityHttp.CorrelationId(context.HttpContext);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                Contracts.IdentityHttp.Problem("TOKEN_INVALID", 401, "Authentication token is invalid", correlationId),
                context.HttpContext.RequestAborted);
        }
    };
}
