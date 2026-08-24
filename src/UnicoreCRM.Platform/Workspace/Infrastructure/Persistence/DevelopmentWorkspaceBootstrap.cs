using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed partial class DevelopmentWorkspaceBootstrap(
    IHostEnvironment environment,
    IServiceScopeFactory scopeFactory,
    IOptions<WorkspaceOptions> options,
    ILogger<DevelopmentWorkspaceBootstrap> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var bootstrap = options.Value.DevelopmentBootstrap;
        if (!environment.IsDevelopment() || !bootstrap.Enabled)
            return;
        Validate(bootstrap);

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WorkspaceDbContext>();
        if (bootstrap.ApplyMigrations)
            await dbContext.Database.MigrateAsync(cancellationToken);

        var identityLookup = scope.ServiceProvider.GetRequiredService<IDevelopmentIdentityReferenceLookup>();
        var identity = await identityLookup.FindActiveByEmailAsync(bootstrap.IdentityEmail, cancellationToken)
            ?? throw new InvalidOperationException("The Development Workspace bootstrap identity must already be an active IdentityAuth account.");
        var now = TimeProvider.System.GetUtcNow();
        var memberWorkspace = await EnsureWorkspaceAsync(dbContext, bootstrap.MemberWorkspace, now, cancellationToken);
        var nonMemberWorkspace = await EnsureWorkspaceAsync(dbContext, bootstrap.NonMemberWorkspace, now, cancellationToken);
        if (!await dbContext.Memberships.AnyAsync(
                membership => membership.WorkspaceId == memberWorkspace.WorkspaceId && membership.AccountId == identity.AccountId,
                cancellationToken))
        {
            dbContext.Memberships.Add(new WorkspaceMembership(memberWorkspace.WorkspaceId, identity.AccountId, identity.MemberId, now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Development Workspace bootstrap ensured member workspace {MemberWorkspaceId} and non-member workspace {NonMemberWorkspaceId}.",
            memberWorkspace.WorkspaceId,
            nonMemberWorkspace.WorkspaceId);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<WorkspaceDefinition> EnsureWorkspaceAsync(
        WorkspaceDbContext dbContext,
        DevelopmentWorkspaceOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var workspace = await dbContext.Workspaces.SingleOrDefaultAsync(item => item.Key == options.Key, cancellationToken);
        if (workspace is null)
        {
            workspace = new WorkspaceDefinition(options.Key, options.Name, options.LogoText, now);
            dbContext.Workspaces.Add(workspace);
            dbContext.BootstrapProjections.Add(new WorkspaceBootstrapProjection(
                workspace.WorkspaceId,
                options.Locale,
                options.TimeZone,
                options.BaseCurrency,
                JsonSerializer.Serialize(options.Capabilities.Distinct(StringComparer.Ordinal).ToArray()),
                JsonSerializer.Serialize(options.EnabledModuleKeys.Distinct(StringComparer.Ordinal).ToArray()),
                JsonSerializer.Serialize(options.AvailableProductSpaces.Distinct(StringComparer.Ordinal).ToArray())));
        }
        return workspace;
    }

    private static void Validate(DevelopmentWorkspaceBootstrapOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.IdentityEmail))
            throw new InvalidOperationException("Development Workspace bootstrap requires an IdentityEmail from external configuration.");
        ValidateWorkspace(options.MemberWorkspace, nameof(options.MemberWorkspace));
        ValidateWorkspace(options.NonMemberWorkspace, nameof(options.NonMemberWorkspace));
        if (string.Equals(options.MemberWorkspace.Key, options.NonMemberWorkspace.Key, StringComparison.Ordinal))
            throw new InvalidOperationException("Development Workspace bootstrap workspace keys must be distinct.");
    }

    private static void ValidateWorkspace(DevelopmentWorkspaceOptions options, string name)
    {
        var valid = WorkspaceKeyPattern().IsMatch(options.Key)
                    && options.Name.Length is >= 1 and <= 200
                    && options.LogoText.Length is >= 1 and <= 8
                    && options.Locale is "vi" or "en"
                    && options.TimeZone.Length is >= 1 and <= 100
                    && CurrencyPattern().IsMatch(options.BaseCurrency)
                    && options.AvailableProductSpaces.Length > 0
                    && options.AvailableProductSpaces.All(value => value is "crm" or "studio" or "people");
        if (!valid)
            throw new InvalidOperationException($"Development Workspace bootstrap {name} does not satisfy the Workspace contract.");
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkspaceKeyPattern();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();
}
