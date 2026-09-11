using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Customers.Contracts;

internal sealed record CustomerRelationshipTargetResolution(
    bool CanReadResource,
    IReadOnlyDictionary<string, string?> VisibleTargets,
    IReadOnlySet<string> MutationEligibleTargetIds)
{
    internal bool IsMutationEligible(string customerId) => MutationEligibleTargetIds.Contains(customerId);
}

internal interface ICustomerRelationshipTargetParticipant
{
    Task<CustomerRelationshipTargetResolution> ResolveVisibleAsync(
        TrustedWorkspaceContext trusted,
        IReadOnlyCollection<string> customerIds,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken);
}
