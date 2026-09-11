using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Organizations.Contracts;

internal sealed record OrganizationCustomerSubject(string OrganizationId, string DisplayName, string? Email, string? Phone, bool IsEligible);
internal interface IOrganizationCustomerSubjectParticipant
{
    Task<OrganizationCustomerSubject?> ResolveVisibleAsync(TrustedWorkspaceContext trusted, string organizationId,
        string requestId, string correlationId, CancellationToken cancellationToken);
}
