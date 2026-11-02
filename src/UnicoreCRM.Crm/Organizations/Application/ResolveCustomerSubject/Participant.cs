using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Organizations.Application.ResolveCustomerSubject;

internal sealed class Participant(OrganizationAuthorization authorization, IOrganizationsPersistence persistence, TimeProvider timeProvider)
    : IOrganizationCustomerSubjectParticipant, IOrganizationCustomerListIdentityParticipant
{
    public async Task<IReadOnlyDictionary<string, string>> ResolveVisibleAsync(TrustedWorkspaceContext trusted,
        IReadOnlyCollection<string> organizationIds, string requestId, string correlationId, CancellationToken cancellationToken)
    {
        if (organizationIds.Count > 250) throw new ArgumentOutOfRangeException(nameof(organizationIds));
        var visible = new Dictionary<string, string>(StringComparer.Ordinal);
        if (organizationIds.Count == 0) return visible;
        var metadata = new OrganizationRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, cancellationToken);
        if (!access.IsSuccess || access.Value!.Trusted.WorkspaceId != trusted.WorkspaceId
            || !access.Value.Authorization.CanRead("displayName")) return visible;
        var records = await persistence.ReadOrganizationsAsync(trusted.WorkspaceId, organizationIds, cancellationToken);
        foreach (var record in records)
        {
            if (await authorization.EnforceRecordAsync(access.Value, record, "listCustomerIdentities", metadata, cancellationToken) is not null) continue;
            var projected = OrganizationFieldSecurity.Project(OrganizationProjection.Document(record), access.Value.Authorization);
            visible[record.OrganizationId] = projected.DisplayName;
            persistence.AddReadAudit(new OrganizationReadAuditRecord("listCustomerIdentities", trusted.WorkspaceId,
                trusted.MemberId, record.OrganizationId, requestId, correlationId, record.Version, timeProvider.GetUtcNow()));
        }
        if (visible.Count > 0) await persistence.SaveChangesAsync(cancellationToken);
        return visible;
    }

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
            organization.Status != "archived", organization.Version);
    }

}
