using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Organizations.Application.ResolveCustomerSubject;

internal sealed class Participant(OrganizationAuthorization authorization, IOrganizationsPersistence persistence, TimeProvider timeProvider)
    : IOrganizationCustomerSubjectParticipant
{
    public async Task<OrganizationCustomerSubject?> ResolveVisibleAsync(TrustedWorkspaceContext trusted, string organizationId,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var metadata = new OrganizationRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, cancellationToken);
        if (!access.IsSuccess || access.Value!.Trusted.WorkspaceId != trusted.WorkspaceId) return null;
        var organization = await persistence.ReadOrganizationAsync(trusted.WorkspaceId, organizationId, cancellationToken);
        if (organization is null || await authorization.EnforceRecordAsync(access.Value, organization,
            "resolveCustomerAccountSubject", metadata, cancellationToken) is not null) return null;
        persistence.AddReadAudit(new OrganizationReadAuditRecord("resolveCustomerAccountSubject", trusted.WorkspaceId,
            trusted.MemberId, organization.OrganizationId, requestId, correlationId, organization.Version, timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        var projected = OrganizationFieldSecurity.Project(OrganizationProjection.Document(organization), access.Value.Authorization);
        return new(projected.Id, projected.DisplayName, projected.Email, projected.Phone,
            organization.Status != "archived");
    }
}
