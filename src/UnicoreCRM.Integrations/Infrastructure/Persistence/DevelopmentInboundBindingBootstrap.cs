using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnicoreCRM.Integrations.Application;
using UnicoreCRM.Integrations.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Integrations.Infrastructure.Persistence;

internal sealed partial class DevelopmentInboundBindingBootstrap(
    IHostEnvironment environment,
    ITrustedWorkspaceMemberResolver memberResolver,
    IWebhookSecretProvider secretProvider,
    IntegrationsDbContext dbContext,
    IOptions<IntegrationsOptions> options,
    ILogger<DevelopmentInboundBindingBootstrap> logger)
{
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var bootstrap = options.Value.DevelopmentBootstrap;
        if (!environment.IsDevelopment() || !bootstrap.Enabled)
            return;
        Validate(bootstrap);

        var trusted = await memberResolver.ResolveActiveMemberAsync(
            bootstrap.WorkspaceId,
            bootstrap.DelegatedMemberId,
            cancellationToken);
        if (trusted is null)
            throw new InvalidOperationException("The Development inbound binding requires an active delegated Workspace member.");

        var secret = secretProvider.Resolve(bootstrap.SecretReference);
        if (secret is null || secret.Length < 32)
            throw new InvalidOperationException("The Development inbound binding secret must come from external configuration and contain at least 32 characters.");

        var existing = await dbContext.InboundBindings.SingleOrDefaultAsync(
            item => item.IntegrationId == bootstrap.IntegrationId,
            cancellationToken);
        if (existing is null)
        {
            dbContext.InboundBindings.Add(new InboundIntegrationBinding(
                bootstrap.IntegrationId,
                bootstrap.ProviderCode,
                bootstrap.WorkspaceId,
                bootstrap.DelegatedMemberId,
                bootstrap.SecretReference,
                bootstrap.BindingEnabled,
                TimeProvider.System.GetUtcNow()));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else if (existing.ProviderCode != bootstrap.ProviderCode
                 || existing.WorkspaceId != bootstrap.WorkspaceId
                 || existing.DelegatedMemberId != bootstrap.DelegatedMemberId
                 || existing.SecretReference != bootstrap.SecretReference
                 || existing.IsEnabled != bootstrap.BindingEnabled)
        {
            throw new InvalidOperationException("Existing Development inbound binding does not match external configuration.");
        }

        logger.LogInformation(
            "Development inbound Integration binding {IntegrationId} is configured for Workspace {WorkspaceId}.",
            bootstrap.IntegrationId,
            bootstrap.WorkspaceId);
    }

    private static void Validate(DevelopmentInboundBindingOptions bootstrap)
    {
        if (!EntityIdPattern().IsMatch(bootstrap.IntegrationId)
            || bootstrap.ProviderCode != "generic-signed-json"
            || !EntityIdPattern().IsMatch(bootstrap.WorkspaceId)
            || !EntityIdPattern().IsMatch(bootstrap.DelegatedMemberId)
            || !SecretReferencePattern().IsMatch(bootstrap.SecretReference))
        {
            throw new InvalidOperationException("Development inbound binding configuration is invalid.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,159}$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretReferencePattern();
}
