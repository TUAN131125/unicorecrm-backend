using System.Text.Json;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.AccessDirectory;

internal sealed class DirectoryComposer(
    IAccessDirectoryPersistence persistence,
    IWorkspaceAccessDirectorySource workspaceSource,
    IIdentityAccessDirectoryProfileSource identitySource,
    TimeProvider timeProvider)
{
    internal async Task<AccessOperationResult<WorkspaceAccessDirectoryDocument>> ComposeAsync(
        string workspaceId,
        CancellationToken cancellationToken)
    {
        var access = await persistence.ReadDirectoryAsync(workspaceId, cancellationToken);

        WorkspaceAccessDirectorySnapshot? workspace;
        IReadOnlyList<IdentityAccessDirectoryProfile> identityProfiles;
        try
        {
            workspace = await workspaceSource.ReadAsync(workspaceId, cancellationToken);
            if (!Valid(workspace, workspaceId))
                return AccessOperationResult<WorkspaceAccessDirectoryDocument>.Failure(AccessErrors.IntegrationUnavailable());
            var accountIds = workspace!.Memberships
                .Select(item => item.AccountId)
                .Where(item => item is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            identityProfiles = await identitySource.ReadAsync(accountIds, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return AccessOperationResult<WorkspaceAccessDirectoryDocument>.Failure(AccessErrors.IntegrationUnavailable());
        }

        if (!ValidIdentity(identityProfiles))
            return AccessOperationResult<WorkspaceAccessDirectoryDocument>.Failure(AccessErrors.IntegrationUnavailable());

        var identityByAccount = identityProfiles.ToDictionary(item => item.AccountId, StringComparer.Ordinal);
        var invitationsByMembership = workspace!.Invitations
            .GroupBy(item => item.MembershipId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.InvitationId, StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var rolesById = access.Roles.ToDictionary(item => item.RoleId, StringComparer.Ordinal);
        var assignmentsByMembership = access.Assignments
            .GroupBy(item => item.MembershipId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.RoleId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        var members = workspace.Memberships
            .OrderBy(item => item.MembershipId, StringComparer.Ordinal)
            .Select(item => new WorkspaceAccessMemberDocument(
                item.MembershipId,
                item.MemberId,
                workspace.WorkspaceId,
                workspace.WorkspaceKey,
                workspace.Name,
                item.Status,
                workspace.LogoText,
                item.TeamIds.Order(StringComparer.Ordinal).ToArray(),
                assignmentsByMembership.GetValueOrDefault(item.MembershipId) ?? [],
                item.Source,
                item.Version,
                item.AccountId,
                item.CreatedAt))
            .ToArray();

        var profiles = new List<WorkspaceMemberProfileDocument>();
        foreach (var membership in workspace.Memberships.OrderBy(item => item.MembershipId, StringComparer.Ordinal))
        {
            var account = membership.AccountId is not null
                ? identityByAccount.GetValueOrDefault(membership.AccountId)
                : null;
            invitationsByMembership.TryGetValue(membership.MembershipId, out var invitation);
            var displayName = account?.DisplayName ?? invitation?.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName))
                continue;
            var activeRoleIds = (assignmentsByMembership.GetValueOrDefault(membership.MembershipId) ?? [])
                .Where(roleId => rolesById.GetValueOrDefault(roleId)?.IsActive == true)
                .ToArray();
            var roleLabel = activeRoleIds.Length == 1 ? rolesById[activeRoleIds[0]].Name : null;
            profiles.Add(new WorkspaceMemberProfileDocument(
                membership.MemberId,
                membership.MembershipId,
                displayName,
                AccountSource(membership.Source),
                membership.AccountId,
                account?.Email ?? invitation?.Email,
                account?.Status is "ACTIVE" or "SUSPENDED" ? account.Status : null,
                roleLabel,
                account?.ProvisionedAt));
        }

        var capabilitiesByRole = access.Capabilities
            .GroupBy(item => item.RoleId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Capability).Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var roles = access.Roles
            .OrderBy(item => item.RoleId, StringComparer.Ordinal)
            .Select(item => new AccessRoleDocument(
                item.RoleId,
                item.WorkspaceId,
                item.Name,
                item.IsActive,
                capabilitiesByRole.GetValueOrDefault(item.RoleId) ?? [],
                item.Version,
                item.Description,
                item.SourceTemplateId))
            .ToArray();
        var assignments = access.Assignments
            .OrderBy(item => item.AssignmentId, StringComparer.Ordinal)
            .Select(item => new RoleAssignmentDocument(item.AssignmentId, item.WorkspaceId, item.MembershipId, item.RoleId))
            .ToArray();
        var scopes = access.DataScopes
            .OrderBy(item => item.RoleId, StringComparer.Ordinal)
            .ThenBy(item => item.ResourceKey, StringComparer.Ordinal)
            .Select(item => new AccessDataScopePolicyDocument(
                item.PolicyId,
                item.WorkspaceId,
                item.RoleId,
                item.ResourceKey,
                AccessDirectoryWire.ToWire(item.Scope),
                item.Scope == Domain.AccessDataScope.Custom ? OwnerIds(item.AllowedOwnerIdsJson) : null))
            .ToArray();
        var fieldSecurity = access.FieldSecurity
            .OrderBy(item => item.RoleId, StringComparer.Ordinal)
            .ThenBy(item => item.ResourceKey, StringComparer.Ordinal)
            .ThenBy(item => item.FieldKey, StringComparer.Ordinal)
            .Select(item => new AccessFieldSecurityPolicyDocument(
                item.PolicyId,
                item.WorkspaceId,
                item.RoleId,
                item.ResourceKey,
                item.FieldKey,
                AccessDirectoryWire.ToWire(item.Access)))
            .ToArray();
        var invitations = workspace.Invitations
            .OrderBy(item => item.InvitationId, StringComparer.Ordinal)
            .Select(item => new WorkspaceInvitationDocument(
                item.InvitationId,
                item.MembershipId,
                item.WorkspaceId,
                item.Email,
                item.DisplayName,
                item.RoleIds.Order(StringComparer.Ordinal).ToArray(),
                item.TeamIds.Order(StringComparer.Ordinal).ToArray(),
                item.Status,
                item.CreatedAt,
                item.LastSentAt,
                item.ExpiresAt,
                item.Version,
                item.AcceptedAt,
                item.RevokedAt))
            .ToArray();

        var document = new WorkspaceAccessDirectoryDocument(
            workspaceId,
            access.Revision,
            timeProvider.GetUtcNow(),
            members,
            profiles.ToArray(),
            invitations,
            roles,
            assignments,
            scopes,
            fieldSecurity);
        return AccessOperationResult<WorkspaceAccessDirectoryDocument>.Success(document);
    }

    private static bool Valid(WorkspaceAccessDirectorySnapshot? snapshot, string workspaceId)
    {
        if (snapshot is null
            || !string.Equals(snapshot.WorkspaceId, workspaceId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(snapshot.WorkspaceKey)
            || string.IsNullOrWhiteSpace(snapshot.Name)
            || string.IsNullOrWhiteSpace(snapshot.LogoText)
            || snapshot.Memberships.GroupBy(item => item.MembershipId, StringComparer.Ordinal).Any(group => group.Count() != 1)
            || snapshot.Memberships.GroupBy(item => item.MemberId, StringComparer.Ordinal).Any(group => group.Count() != 1)
            || snapshot.Invitations.GroupBy(item => item.InvitationId, StringComparer.Ordinal).Any(group => group.Count() != 1)
            || snapshot.Invitations.Any(item => !string.Equals(item.WorkspaceId, workspaceId, StringComparison.Ordinal)))
        {
            return false;
        }
        return snapshot.Memberships.All(item =>
            !string.IsNullOrWhiteSpace(item.MembershipId)
            && !string.IsNullOrWhiteSpace(item.MemberId)
            && item.Status is "active" or "suspended" or "invited"
            && item.Source is "seed" or "invitation" or "direct_provisioning" or "external_identity");
    }

    private static bool ValidIdentity(IReadOnlyList<IdentityAccessDirectoryProfile> profiles) =>
        profiles.All(item => !string.IsNullOrWhiteSpace(item.AccountId) && !string.IsNullOrWhiteSpace(item.DisplayName))
        && profiles.GroupBy(item => item.AccountId, StringComparer.Ordinal).All(group => group.Count() == 1);

    private static string AccountSource(string source) => source switch
    {
        "seed" => "seed",
        "direct_provisioning" => "direct",
        "invitation" => "invitation",
        "external_identity" => "external",
        _ => throw new InvalidOperationException("Workspace membership source is not representable.")
    };

    private static IReadOnlyList<string> OwnerIds(string json) =>
        (JsonSerializer.Deserialize<string[]>(json) ?? []).Order(StringComparer.Ordinal).ToArray();
}
