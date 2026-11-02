using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.CheckQuoteApprovalAvailability;

internal sealed class QuoteApprovalAvailability(ICurrentWorkspace currentWorkspace,
    IWorkspaceAccessDirectorySource directory, ITrustedWorkspaceMemberResolver members,
    IAccessControlPersistence persistence, RecordAccessFactProviderRegistry providers) : IQuoteApprovalAvailability
{
    public async Task<bool> HasEligibleApproverAsync(string workspaceId, string? quoteOwnerId, string creatorId,
        string requesterId, IReadOnlyList<string> requiredWriteFields, IReadOnlyList<string> requiredReadFields,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentWorkspace.IsResolved || currentWorkspace.Require().WorkspaceId != workspaceId
            || string.IsNullOrWhiteSpace(creatorId) || string.IsNullOrWhiteSpace(requesterId)
            || requiredWriteFields is not { Count: > 0 and <= 64 }
            || requiredReadFields is not { Count: > 0 and <= 64 }) return false;
        var descriptor = providers.Find("quotes")?.Descriptor;
        if (descriptor is null || descriptor.ReadCapability != "quotes.read"
            || requiredWriteFields.Concat(requiredReadFields).Any(field => !descriptor.EnforceableFields.ContainsKey(field))) return false;

        var snapshot = await directory.ReadAsync(workspaceId, cancellationToken);
        if (snapshot is null || snapshot.WorkspaceId != workspaceId) return false;
        foreach (var member in snapshot.Memberships.Where(member => member.Status == "active"
                     && member.MemberId != creatorId && member.MemberId != requesterId)
                     .DistinctBy(member => member.MemberId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trusted = await members.ResolveActiveMemberAsync(workspaceId, member.MemberId, cancellationToken);
            if (trusted is null || trusted.WorkspaceId != workspaceId || trusted.MemberId != member.MemberId
                || trusted.MembershipId != member.MembershipId) continue;
            var state = await persistence.LoadEffectiveStateAsync(workspaceId, trusted.MembershipId, cancellationToken);
            var policy = EffectiveAuthorizationPolicy.Evaluate(state);
            if (!policy.Capabilities.Contains("quotes.read", StringComparer.Ordinal)
                || !policy.Capabilities.Contains("quotes.approve", StringComparer.Ordinal)) continue;
            var scope = RecordAccessPolicy.ResolveScope(policy.DataScopes, "quotes");
            if (RecordAccessPolicy.EvaluateScope(scope, true, true, quoteOwnerId, trusted.MemberId).Outcome
                != RecordScopeOutcome.Allowed) continue;
            // Full Quote mutation responses cannot withhold required representation fields.
            // Masked is withheld by the canonical policy, so only readable fields qualify.
            if (descriptor.EnforceableFields.Any(field => field.Value
                    && RecordAccessPolicy.ResolveFieldAccess(policy.FieldSecurity, "quotes", field.Key)
                        is AccessFieldAccess.Hidden or AccessFieldAccess.Masked)) continue;
            if (requiredReadFields.Any(field => RecordAccessPolicy.ResolveFieldAccess(policy.FieldSecurity,
                    "quotes", field) is AccessFieldAccess.Hidden or AccessFieldAccess.Masked)) continue;
            if (requiredWriteFields.Any(field => RecordAccessPolicy.ResolveFieldAccess(policy.FieldSecurity,
                    "quotes", field) != AccessFieldAccess.ReadWrite)) continue;
            return true;
        }
        return false;
    }
}
