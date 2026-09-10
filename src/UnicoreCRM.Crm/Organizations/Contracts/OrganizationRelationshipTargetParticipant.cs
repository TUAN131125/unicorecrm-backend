using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Organizations.Contracts;

internal sealed record OrganizationRelationshipTargetResolution(
    bool CanReadResource,
    IReadOnlyDictionary<string, string?> VisibleTargets,
    IReadOnlySet<string> MutationEligibleTargetIds)
{
    internal bool IsMutationEligible(string organizationId) => MutationEligibleTargetIds.Contains(organizationId);
}

internal interface IOrganizationRelationshipTargetParticipant
{
    Task<OrganizationRelationshipTargetResolution> ResolveVisibleAsync(
        TrustedWorkspaceContext trusted,
        IReadOnlyCollection<string> organizationIds,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken);
}
