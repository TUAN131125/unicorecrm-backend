using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Organizations.Application.ListOrganizations;

internal sealed record Query(OrganizationRequestMetadata Metadata,string? Q,string? Status,string? Industry,string? SizeBand,string? OwnerId,string? Cursor,int Limit);

internal sealed class Handler(
    OrganizationAuthorization authorization,
    IOrganizationsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<OrganizationOperationResult<OrganizationListResponse>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return OrganizationOperationResult<OrganizationListResponse>.Failure(access.Error!);

        IReadOnlyList<Organization> organizations = access.Value!.Authorization.ScopeFilter switch
        {
            RecordAccessScopeFilter.Workspace => await persistence.ReadOrganizationsAsync(
                access.Value.Trusted.WorkspaceId,
                cancellationToken),
            RecordAccessScopeFilter.OwnedByMember => (await persistence.ReadOrganizationsAsync(
                access.Value.Trusted.WorkspaceId,
                cancellationToken)).Where(item => item.OwnerId == access.Value.Authorization.ScopeOwnerMemberId).ToArray(),
            _ => []
        };
        if (query.Status is null) organizations = organizations.Where(item => item.Status != "archived").ToArray();
        if (query.Status is not null) organizations = organizations.Where(x => x.Status == query.Status).ToArray();
        if (query.Industry is not null) organizations = organizations.Where(x => string.Equals(x.Profile.Industry, query.Industry, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (query.SizeBand is not null) organizations = organizations.Where(x => string.Equals(x.Profile.SizeBand, query.SizeBand, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (query.OwnerId is not null) organizations = organizations.Where(x => x.OwnerId == query.OwnerId).ToArray();
        if (!string.IsNullOrWhiteSpace(query.Q)) { var q=query.Q.Trim(); organizations=organizations.Where(x => x.DisplayName.Contains(q,StringComparison.OrdinalIgnoreCase)||(x.Profile.LegalName?.Contains(q,StringComparison.OrdinalIgnoreCase)??false)||(x.Profile.TaxCode?.Contains(q,StringComparison.OrdinalIgnoreCase)??false)||(x.Profile.Domain?.Contains(q,StringComparison.OrdinalIgnoreCase)??false)).ToArray(); }
        organizations = organizations.Take(query.Limit).ToArray();

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
        var items = organizations
                .Select(organization => OrganizationFieldSecurity.Project(
                    OrganizationProjection.Document(organization),
                    access.Value.Authorization))
                .ToArray();
        return OrganizationOperationResult<OrganizationListResponse>.Success(new(items, new(null, false)));
    }
}
