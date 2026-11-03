using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Organizations.Application.ResolveCustomerSubject;

internal sealed class Participant(OrganizationAuthorization authorization, IOrganizationsPersistence persistence, TimeProvider timeProvider)
    : IOrganizationCustomerSubjectParticipant, IOrganizationCustomerListIdentityParticipant
{
    public async Task<UnicoreCRM.Crm.Customers.Contracts.CustomerSourceSearchPlan?> SearchPlanAsync(TrustedWorkspaceContext trusted,
        string requestId, string correlationId, CancellationToken ct)
    {
        var grant = await authorization.AuthorizeAsync(new(requestId, correlationId), ct);
        if (!grant.IsSuccess || grant.Value!.Trusted.WorkspaceId != trusted.WorkspaceId) return null;
        var policy = grant.Value.Authorization;
        if (policy.ScopeFilter is not (UnicoreCRM.Platform.AccessControl.Contracts.RecordAccessScopeFilter.Workspace or UnicoreCRM.Platform.AccessControl.Contracts.RecordAccessScopeFilter.OwnedByMember)) return null;
        var owner = policy.ScopeFilter == UnicoreCRM.Platform.AccessControl.Contracts.RecordAccessScopeFilter.OwnedByMember ? policy.ScopeOwnerMemberId : null;
        if (policy.ScopeFilter == UnicoreCRM.Platform.AccessControl.Contracts.RecordAccessScopeFilter.OwnedByMember && owner is null) return null;
        return persistence.CustomerSearchPlan(trusted.WorkspaceId, owner, new[] { "displayName", "legalName", "email", "phone", "taxCode" }.Where(policy.CanRead).ToHashSet(StringComparer.Ordinal));
    }
    public async Task<IReadOnlySet<string>> MatchVisibleAsync(TrustedWorkspaceContext trusted, IReadOnlyCollection<string> ids,
        string search, string requestId, string correlationId, CancellationToken ct)
    {
        if(ids.Count>250)throw new ArgumentOutOfRangeException(nameof(ids));
        var metadata=new OrganizationRequestMetadata(requestId,correlationId);
        var grant=await authorization.AuthorizeAsync(metadata, ct);
        var matches=new HashSet<string>(StringComparer.Ordinal);
        if(!grant.IsSuccess || grant.Value!.Trusted.WorkspaceId!=trusted.WorkspaceId)return matches;
        var records=await persistence.ReadOrganizationsAsync(trusted.WorkspaceId,ids,ct);
        foreach(var row in records)
        {
            var projected=OrganizationProjection.Document(row);
            if(!((grant.Value.Authorization.CanRead("displayName") && projected.DisplayName?.Contains(search,StringComparison.OrdinalIgnoreCase)==true) || (grant.Value.Authorization.CanRead("legalName") && projected.LegalName?.Contains(search,StringComparison.OrdinalIgnoreCase)==true) || (grant.Value.Authorization.CanRead("email") && projected.Email?.Contains(search,StringComparison.OrdinalIgnoreCase)==true) || (grant.Value.Authorization.CanRead("phone") && projected.Phone?.Contains(search,StringComparison.OrdinalIgnoreCase)==true) || (grant.Value.Authorization.CanRead("taxCode") && projected.TaxCode?.Contains(search,StringComparison.OrdinalIgnoreCase)==true)))continue;
            if(await authorization.EnforceRecordAsync(grant.Value,row,"searchCustomerIdentity",metadata,ct) is not null)continue;
            matches.Add(row.OrganizationId);
        }
        return matches;
    }


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
