using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Organizations.Application.ValidateTaskReference;

internal sealed class OrganizationTaskReferenceAuthority(OrganizationAuthorization authorization,
    IOrganizationsPersistence persistence) : IOrganizationTaskReferenceAuthority
{
    private static readonly RecordAccessRepresentation Representation = RecordAccessRepresentation.Create(
        "organization.taskReference", OrganizationFieldSecurity.FieldKeys.Where(field => field != "id").ToArray());

    public async Task<bool> CanReferenceAsync(string workspaceId, string organizationId,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var metadata = new OrganizationRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, OrganizationCapabilities.Read, cancellationToken, Representation);
        if (!access.IsSuccess) return false;
        if (access.Value!.Trusted.WorkspaceId != workspaceId
            || !Regex.IsMatch(organizationId, "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant))
            return false;
        var organization = await persistence.ReadOrganizationAsync(workspaceId, organizationId, cancellationToken);
        if (organization is null || await authorization.EnforceRecordAsync(access.Value, organization,
            "validateTaskOrganizationReference", metadata, cancellationToken) is not null)
            return false;
        // Existing Organizations relationship-target contract admits non-archived records,
        // including inactive. Validation-point guarantee; no cross-owner lifecycle lock.
        return organization.Status != "archived";
    }
}
