using System.Text.Json;
using System.Text.RegularExpressions;
using UnicoreCRM.Platform.Workspace.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Application.ProvisionInitialWorkspace;

/// <summary>
/// The Workspace-owned participant of the Initial Workspace Provisioning workflow. It owns the
/// Workspace aggregate identifier, the Workspace key, the ACTIVE creator membership identifier
/// and the account-scoped provisioning anchor. It never accepts an aggregate identifier, a
/// membership status or a Workspace key from the caller.
///
/// The anchor is committed as <c>AccessPending</c> together with the Workspace, so an attempt
/// that stops before the AccessControl participant commits leaves an authoritative
/// outstanding-work record rather than a silently broken Workspace.
/// </summary>
internal sealed partial class InitialWorkspaceProvisioningService(
    IInitialWorkspaceProvisioningPersistence persistence,
    TimeProvider timeProvider) : IInitialWorkspaceProvisioning
{
    private const int WorkspaceKeyAttempts = 5;
    private const int WorkspaceKeySlugLength = 100;

    public async Task<InitialWorkspaceProvisioningResult> EnsureInitialWorkspaceAsync(
        InitialWorkspaceProvisioningRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await ReadExistingAsync(request.AccountId, cancellationToken);
        if (existing is not null)
            return existing;
        if (await persistence.HasActiveMembershipAsync(request.AccountId, cancellationToken))
            return new InitialWorkspaceProvisioningResult(InitialWorkspaceProvisioningStatus.RejectedExistingWorkspace, null, null);

        var now = timeProvider.GetUtcNow();
        for (var attempt = 0; attempt < WorkspaceKeyAttempts; attempt++)
        {
            var workspaceKey = await ReserveKeyAsync(request.Name, cancellationToken);
            if (workspaceKey is null)
                continue;

            var workspace = new WorkspaceDefinition(workspaceKey, request.Name, request.LogoText, now);
            var membership = new WorkspaceMembership(workspace.WorkspaceId, request.AccountId, request.MemberId, now);
            var configuration = new WorkspaceBootstrapProjection(
                workspace.WorkspaceId,
                request.Configuration.Locale,
                request.Configuration.TimeZone,
                request.Configuration.BaseCurrency,
                JsonSerializer.Serialize(Array.Empty<string>()),
                JsonSerializer.Serialize(request.Configuration.EnabledModuleKeys.ToArray()),
                JsonSerializer.Serialize(request.Configuration.AvailableProductSpaces.ToArray()));
            var provisioning = new InitialWorkspaceProvisioningRecord(
                request.AccountId,
                request.MemberId,
                workspace.WorkspaceId,
                membership.MembershipId,
                request.IdempotencyKey,
                request.RequestFingerprint,
                now);

            if (await persistence.TryCommitProvisioningAsync(workspace, membership, configuration, provisioning, cancellationToken))
            {
                return new InitialWorkspaceProvisioningResult(
                    InitialWorkspaceProvisioningStatus.Provisioned,
                    WorkspaceProjection.Membership(new WorkspaceMembershipReadModel(
                        membership.MembershipId,
                        workspace.WorkspaceId,
                        workspace.Key,
                        workspace.Name,
                        "active",
                        workspace.LogoText)),
                    now,
                    request.IdempotencyKey,
                    request.RequestFingerprint,
                    true);
            }

            // The write lost the account-scoped uniqueness race or the generated key collided.
            // A concurrent winner is authoritative, so converge on its result instead of retrying.
            var converged = await ReadExistingAsync(request.AccountId, cancellationToken);
            if (converged is not null)
                return converged;
        }

        throw new InvalidOperationException("Initial Workspace provisioning could not reserve a Workspace key.");
    }

    private async Task<InitialWorkspaceProvisioningResult?> ReadExistingAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        var record = await persistence.FindProvisioningRecordAsync(accountId, cancellationToken);
        if (record is null)
            return null;
        var membership = await persistence.FindMembershipAsync(record.WorkspaceId, record.MembershipId, cancellationToken)
            ?? throw new InvalidOperationException("The recorded initial Workspace membership is missing from Workspace persistence.");
        return new InitialWorkspaceProvisioningResult(
            InitialWorkspaceProvisioningStatus.AlreadyProvisioned,
            WorkspaceProjection.Membership(membership),
            record.ProvisionedAt,
            record.IdempotencyKey,
            record.RequestFingerprint,
            record.State == InitialWorkspaceProvisioningState.AccessPending);
    }

    public async Task<IReadOnlyList<PendingInitialWorkspaceProvisioning>> ListAccessPendingAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var records = await persistence.ListAccessPendingAsync(limit, cancellationToken);
        return records
            .Select(record => new PendingInitialWorkspaceProvisioning(
                record.AccountId,
                record.WorkspaceId,
                record.MembershipId,
                record.ProvisionedAt))
            .ToArray();
    }

    public async Task<IReadOnlyList<InitialWorkspaceAccessAnchor>> ListAccessConvergenceAnchorsAsync(
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var records = await persistence.ListAccessConvergenceAnchorsAsync(offset, limit, cancellationToken);
        return records
            .Select(record => new InitialWorkspaceAccessAnchor(
                record.WorkspaceId,
                record.MembershipId))
            .ToArray();
    }

    public async Task CompleteInitialWorkspaceAsync(string accountId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (!await persistence.TryCompleteProvisioningAsync(accountId, timeProvider.GetUtcNow(), cancellationToken))
            throw new InvalidOperationException("No initial Workspace provisioning anchor exists for the account.");
    }

    private async Task<string?> ReserveKeyAsync(string name, CancellationToken cancellationToken)
    {
        var candidate = ComposeKey(name);
        return await persistence.WorkspaceKeyExistsAsync(candidate, cancellationToken) ? null : candidate;
    }

    /// <summary>
    /// Derives the server-owned Workspace key. The caller never supplies it: the key is a slug of
    /// the Workspace name plus a server-generated suffix, so it always satisfies the contract
    /// pattern and never depends on caller-controlled uniqueness.
    /// </summary>
    private static string ComposeKey(string name)
    {
        var slug = SlugSeparator().Replace(NonSlugCharacter().Replace(name.ToLowerInvariant(), "-"), "-").Trim('-');
        if (slug.Length > WorkspaceKeySlugLength)
            slug = slug[..WorkspaceKeySlugLength].Trim('-');
        if (slug.Length == 0)
            slug = "workspace";
        return $"{slug}-{Guid.NewGuid():N}"[..Math.Min(slug.Length + 9, 120)];
    }

    [GeneratedRegex("[^a-z0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlugCharacter();

    [GeneratedRegex("-{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex SlugSeparator();
}
