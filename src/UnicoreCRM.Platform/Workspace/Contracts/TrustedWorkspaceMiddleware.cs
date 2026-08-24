using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using UnicoreCRM.Platform.Workspace.Application.Common;

namespace UnicoreCRM.Platform.Workspace.Contracts;

/// <summary>
/// Establishes the trusted workspace for endpoints marked with <see cref="WorkspaceRequiredMetadata"/>.
/// The requested workspace arrives as untrusted transport input; it becomes authority only after an
/// active membership is resolved for the authenticated account.
/// </summary>
internal sealed class TrustedWorkspaceMiddleware(RequestDelegate next)
{
    internal const string WorkspaceHeaderName = "X-Workspace-Id";

    public async Task InvokeAsync(
        HttpContext context,
        IWorkspaceContextResolver resolver,
        ITrustedWorkspaceSetter setter)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<WorkspaceRequiredMetadata>() is null)
        {
            await next(context);
            return;
        }

        var correlationId = WorkspaceHttp.TryRequest(context, out var request, out var metadataError)
            ? request!.CorrelationId
            : WorkspaceHttpCorrelation(context);
        if (metadataError is not null)
        {
            await metadataError.ExecuteAsync(context);
            return;
        }

        var accountId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var memberId = context.User.FindFirst("member_id")?.Value;
        if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(memberId))
        {
            await WorkspaceHttp.Error(WorkspaceErrors.AuthenticationRequired(), correlationId).ExecuteAsync(context);
            return;
        }

        var requestedWorkspaceId = context.Request.Headers[WorkspaceHeaderName].ToString();
        if (!WorkspaceIdContract.IsValid(requestedWorkspaceId))
        {
            await WorkspaceHttp.Error(WorkspaceErrors.WorkspaceMismatch(), correlationId).ExecuteAsync(context);
            return;
        }

        var trusted = await resolver.ResolveAsync(accountId, memberId, requestedWorkspaceId, context.RequestAborted);
        if (trusted is null)
        {
            await WorkspaceHttp.Error(WorkspaceErrors.AccessDenied(), correlationId).ExecuteAsync(context);
            return;
        }

        setter.Set(trusted);
        await next(context);
    }

    private static string WorkspaceHttpCorrelation(HttpContext context)
    {
        var supplied = context.Request.Headers["X-Correlation-Id"].ToString();
        return supplied.Length is >= 8 and <= 128 ? supplied : context.TraceIdentifier;
    }
}
