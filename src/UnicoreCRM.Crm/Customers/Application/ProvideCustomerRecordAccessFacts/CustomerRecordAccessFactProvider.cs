using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.ProvideCustomerRecordAccessFacts;

internal sealed partial class CustomerRecordAccessFactProvider(ICustomersPersistence persistence) : IRecordAccessFactProvider
{
    private static readonly RecordAccessResourceDescriptor CustomersDescriptor = RecordAccessResourceDescriptor.Create(
        resourceKey: CustomerAuthorization.ResourceKey,
        readCapability: CustomerCapabilities.View.Capability,
        enforceableFields: CustomerFieldSecurity.EnforceableFields);

    public RecordAccessResourceDescriptor Descriptor => CustomersDescriptor;

    public async Task<RecordAccessFacts> ReadFactsAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string recordId,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        if (!EntityIdPattern().IsMatch(recordId))
            return RecordAccessFacts.NotFound;

        var customer = await persistence.ReadCustomerAsync(
            trustedWorkspace.WorkspaceId,
            recordId,
            cancellationToken);
        return customer is null ? RecordAccessFacts.NotFound : CustomerAuthorization.Facts(customer);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
