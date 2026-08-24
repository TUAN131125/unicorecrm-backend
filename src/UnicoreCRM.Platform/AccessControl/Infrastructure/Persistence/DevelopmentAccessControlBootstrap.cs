using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

internal sealed class DevelopmentAccessControlBootstrap(
    IHostEnvironment environment,
    IServiceScopeFactory scopeFactory,
    IOptions<AccessControlOptions> options,
    ILogger<DevelopmentAccessControlBootstrap> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var bootstrap = options.Value.DevelopmentBootstrap;
        if (!environment.IsDevelopment() || !bootstrap.Enabled)
            return;
        var capabilities = Validate(bootstrap);

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AccessControlDbContext>();
        if (bootstrap.ApplyMigrations)
            await dbContext.Database.MigrateAsync(cancellationToken);

        var identityLookup = scope.ServiceProvider.GetRequiredService<IDevelopmentIdentityReferenceLookup>();
        var identity = await identityLookup.FindActiveByEmailAsync(bootstrap.IdentityEmail, cancellationToken)
            ?? throw new InvalidOperationException("The Development AccessControl bootstrap identity must already be an active IdentityAuth account.");
        var workspaceLookup = scope.ServiceProvider.GetRequiredService<IDevelopmentWorkspaceReferenceLookup>();
        var workspace = await workspaceLookup.FindActiveMembershipAsync(
                bootstrap.WorkspaceKey,
                identity.AccountId,
                identity.MemberId,
                cancellationToken)
            ?? throw new InvalidOperationException("The Development AccessControl bootstrap identity must already have an active Workspace membership.");

        var roleName = bootstrap.RoleName.Trim();
        var role = await dbContext.Roles.SingleOrDefaultAsync(
            item => item.WorkspaceId == workspace.WorkspaceId && item.Name == roleName,
            cancellationToken);
        var now = TimeProvider.System.GetUtcNow();
        if (role is null)
        {
            role = new AccessRole(
                workspace.WorkspaceId,
                roleName,
                "Development-only bootstrap role.",
                null,
                now);
            dbContext.Roles.Add(role);
            dbContext.RoleCapabilities.AddRange(capabilities.Select(capability => new RoleCapability(role.RoleId, capability)));
        }
        else
        {
            var existingCapabilities = await dbContext.RoleCapabilities
                .AsNoTracking()
                .Where(item => item.RoleId == role.RoleId)
                .Select(item => item.Capability)
                .OrderBy(item => item)
                .ToArrayAsync(cancellationToken);
            if (!existingCapabilities.SequenceEqual(capabilities, StringComparer.Ordinal))
                throw new InvalidOperationException("Existing Development AccessControl bootstrap state does not match external capability configuration.");
        }

        if (!await dbContext.MembershipRoleAssignments.AnyAsync(
                item => item.WorkspaceId == workspace.WorkspaceId
                        && item.MembershipId == workspace.MembershipId
                        && item.RoleId == role.RoleId,
                cancellationToken))
        {
            dbContext.MembershipRoleAssignments.Add(new MembershipRoleAssignment(
                workspace.WorkspaceId,
                workspace.MembershipId,
                role.RoleId,
                now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Development AccessControl bootstrap ensured role {RoleId} for workspace {WorkspaceId} membership {MembershipId}.",
            role.RoleId,
            workspace.WorkspaceId,
            workspace.MembershipId);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string[] Validate(DevelopmentAccessControlBootstrapOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.IdentityEmail)
            || string.IsNullOrWhiteSpace(options.WorkspaceKey)
            || options.RoleName.Trim().Length is < 1 or > 160)
        {
            throw new InvalidOperationException("Development AccessControl bootstrap requires identity, workspace, and role values from external configuration.");
        }

        try
        {
            var capabilities = options.Capabilities
                .Select(AccessRequirement.ForCanonicalCapability)
                .Select(requirement => requirement.Capability)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (capabilities.Length > 1000)
                throw new InvalidOperationException("Development AccessControl bootstrap cannot exceed the authorization-context capability limit.");
            return capabilities;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("Development AccessControl bootstrap capabilities must use canonical capability identifiers.", exception);
        }
    }
}
