using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;

namespace UnicoreCRM.Crm.Organizations.Application.GetOrganizationOverview;
internal sealed record Query(string OrganizationId, OrganizationRequestMetadata Metadata);
internal sealed class Handler(OrganizationAuthorization authorization, IOrganizationsPersistence persistence,
    IOrganizationContactReadParticipant contacts, TimeProvider timeProvider)
{
    internal async Task<OrganizationOperationResult<OrganizationOverviewReadModel>> HandleAsync(Query query, CancellationToken ct)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, ct);
        if (!access.IsSuccess) return OrganizationOperationResult<OrganizationOverviewReadModel>.Failure(access.Error!);
        var current = access.Value!;
        var organization = await persistence.ReadOrganizationAsync(current.Trusted.WorkspaceId, query.OrganizationId, ct);
        if (organization is null) return OrganizationOperationResult<OrganizationOverviewReadModel>.Failure(OrganizationErrors.NotFound());
        var denied = await authorization.EnforceRecordAsync(current, organization, "getOrganizationOverview", query.Metadata, ct);
        if (denied is not null) return OrganizationOperationResult<OrganizationOverviewReadModel>.Failure(denied);
        // The admitted Overview requires ContactIds and representativeCount. Without
        // contactRefs read access it has no safe partial representation: fail closed,
        // before consulting Contacts, rather than misrepresent withheld data as empty.
        if (!current.Authorization.CanRead("contactRefs"))
            return OrganizationOperationResult<OrganizationOverviewReadModel>.Failure(OrganizationErrors.AccessDenied());
        var ids = await contacts.ReadVisibleActiveContactIdsAsync(current.Trusted, organization.OrganizationId,
            query.Metadata.RequestId, query.Metadata.CorrelationId, ct);
        var allowed = new List<string>();
        if (organization.Status != "archived")
        {
            var update = await authorization.AuthorizeAsync(query.Metadata, OrganizationCapabilities.Update, ct);
            if (update.IsSuccess && OrganizationFieldSecurity.UpdateFieldKeys.Any(update.Value!.Authorization.CanWrite)
                && await authorization.EnforceRecordAsync(update.Value, organization, "overviewUpdate", query.Metadata, ct) is null)
                allowed.Add("updateOrganization");
            var archive = await authorization.AuthorizeAsync(query.Metadata, OrganizationCapabilities.Delete, ct);
            if (archive.IsSuccess && archive.Value!.Authorization.CanWrite("status")
                && await authorization.EnforceRecordAsync(archive.Value, organization, "overviewArchive", query.Metadata, ct) is null)
                allowed.Add("archiveOrganization");
        }
        var now = timeProvider.GetUtcNow();
        persistence.AddReadAudit(new OrganizationReadAuditRecord("getOrganizationOverview", current.Trusted.WorkspaceId,
            current.Trusted.MemberId, organization.OrganizationId, query.Metadata.RequestId,
            query.Metadata.CorrelationId, organization.Version, now));
        await persistence.SaveChangesAsync(ct);
        var document = OrganizationFieldSecurity.Project(OrganizationProjection.Document(organization), current.Authorization);
        return OrganizationOperationResult<OrganizationOverviewReadModel>.Success(new(document, ids, new(ids.Count), [],
            allowed, organization.Version, OrganizationProjection.TimestampValue(now)));
    }
}
