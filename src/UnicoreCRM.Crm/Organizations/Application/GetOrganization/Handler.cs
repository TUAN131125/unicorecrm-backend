using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;

namespace UnicoreCRM.Crm.Organizations.Application.GetOrganization;

internal sealed record Query(string OrganizationId, OrganizationRequestMetadata Metadata);

internal sealed partial class Handler(
    OrganizationAuthorization authorization,
    IOrganizationsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<OrganizationOperationResult<OrganizationDocument>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return OrganizationOperationResult<OrganizationDocument>.Failure(access.Error!);
        if (!EntityIdPattern().IsMatch(query.OrganizationId))
            return OrganizationOperationResult<OrganizationDocument>.Failure(OrganizationErrors.NotFound());

        var organization = await persistence.ReadOrganizationAsync(
            access.Value!.Trusted.WorkspaceId,
            query.OrganizationId,
            cancellationToken);
        if (organization is null)
            return OrganizationOperationResult<OrganizationDocument>.Failure(OrganizationErrors.NotFound());

        var denied = await authorization.EnforceRecordAsync(
            access.Value,
            organization,
            "getOrganization",
            query.Metadata,
            cancellationToken);
        if (denied is not null)
            return OrganizationOperationResult<OrganizationDocument>.Failure(denied);

        persistence.AddReadAudit(new OrganizationReadAuditRecord(
            "getOrganization",
            access.Value.Trusted.WorkspaceId,
            access.Value.Trusted.MemberId,
            organization.OrganizationId,
            query.Metadata.RequestId,
            query.Metadata.CorrelationId,
            organization.Version,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return OrganizationOperationResult<OrganizationDocument>.Success(
            OrganizationFieldSecurity.Project(
                OrganizationProjection.Document(organization),
                access.Value.Authorization));
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
