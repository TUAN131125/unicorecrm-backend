using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Organizations.Application.ProvideOrganizationRecordAccessFacts;

internal sealed partial class OrganizationRecordAccessFactProvider(IOrganizationsPersistence persistence) : IRecordAccessFactProvider
{
    private static readonly RecordAccessResourceDescriptor OrganizationsDescriptor = RecordAccessResourceDescriptor.Create(
        resourceKey: OrganizationAuthorization.ResourceKey,
        readCapability: OrganizationCapabilities.Read.Capability,
        enforceableFields: OrganizationFieldSecurity.EnforceableFields);

    public RecordAccessResourceDescriptor Descriptor => OrganizationsDescriptor;

    public async Task<RecordAccessFacts> ReadFactsAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string recordId,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        if (!EntityIdPattern().IsMatch(recordId))
            return RecordAccessFacts.NotFound;

        var organization = await persistence.ReadOrganizationAsync(
            trustedWorkspace.WorkspaceId,
            recordId,
            cancellationToken);
        return organization is null ? RecordAccessFacts.NotFound : OrganizationAuthorization.Facts(organization);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
