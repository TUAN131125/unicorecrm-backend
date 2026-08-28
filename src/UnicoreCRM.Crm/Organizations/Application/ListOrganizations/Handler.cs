using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Organizations.Application.ListOrganizations;

internal sealed record Query(OrganizationRequestMetadata Metadata);

internal sealed class Handler(
    OrganizationAuthorization authorization,
    IOrganizationsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<OrganizationOperationResult<IReadOnlyList<OrganizationDocument>>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return OrganizationOperationResult<IReadOnlyList<OrganizationDocument>>.Failure(access.Error!);

        IReadOnlyList<Organization> organizations = access.Value!.Authorization.ScopeFilter switch
        {
            RecordAccessScopeFilter.Workspace => await persistence.ReadOrganizationsAsync(
                access.Value.Trusted.WorkspaceId,
                cancellationToken),
            // OWN has no authoritative Organization ownership fact; TEAM and CUSTOM are likewise
            // unresolved. Every non-WORKSPACE scope therefore fails closed before querying rows.
            _ => []
        };

        persistence.AddReadAudit(new OrganizationReadAuditRecord(
            "listOrganizations",
            access.Value.Trusted.WorkspaceId,
            access.Value.Trusted.MemberId,
            null,
            query.Metadata.RequestId,
            query.Metadata.CorrelationId,
            null,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return OrganizationOperationResult<IReadOnlyList<OrganizationDocument>>.Success(
            organizations
                .Select(organization => OrganizationFieldSecurity.Project(
                    OrganizationProjection.Document(organization),
                    access.Value.Authorization))
                .ToArray());
    }
}
