using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Organizations.Contracts;

public sealed record OrganizationCustomerSubject(string OrganizationId, string DisplayName, string? Email, string? Phone, bool IsEligible, long Version);
public interface IOrganizationCustomerSubjectParticipant
{
    Task<OrganizationCustomerSubject?> ResolveVisibleAsync(TrustedWorkspaceContext trusted, string organizationId,
        string requestId, string correlationId, CancellationToken cancellationToken);
}
