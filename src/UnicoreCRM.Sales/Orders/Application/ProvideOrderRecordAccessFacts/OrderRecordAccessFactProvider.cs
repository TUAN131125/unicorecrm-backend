using System.Text.RegularExpressions;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Orders.Application.Common;
using UnicoreCRM.Sales.Orders.Contracts;

namespace UnicoreCRM.Sales.Orders.Application.ProvideOrderRecordAccessFacts;

internal sealed partial class OrderRecordAccessFactProvider(IOrdersPersistence persistence) : IRecordAccessFactProvider
{
    private static readonly RecordAccessResourceDescriptor OrdersDescriptor = RecordAccessResourceDescriptor.Create(
        resourceKey: OrderAuthorization.ResourceKey,
        readCapability: OrderCapabilities.Read.Capability,
        enforceableFields: OrderFieldSecurity.EnforceableFields);

    public RecordAccessResourceDescriptor Descriptor => OrdersDescriptor;

    public async Task<RecordAccessFacts> ReadFactsAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string recordId,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        if (!EntityIdPattern().IsMatch(recordId))
            return RecordAccessFacts.NotFound;

        var order = await persistence.ReadOrderAsync(
            trustedWorkspace.WorkspaceId,
            recordId,
            cancellationToken);
        return order is null ? RecordAccessFacts.NotFound : OrderAuthorization.Facts(order);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
