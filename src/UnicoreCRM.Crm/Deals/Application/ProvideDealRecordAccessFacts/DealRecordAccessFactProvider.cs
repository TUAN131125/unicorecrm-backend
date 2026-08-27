using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.ProvideDealRecordAccessFacts;

/// <summary>
/// The Deals side of the narrow record-access fact boundary. Deals stays authoritative for Deal
/// existence and for the owner member reference record scope is defined against; AccessControl stays
/// authoritative for the decision. It authorizes nothing, queries only the trusted Workspace, and
/// writes no Deal state, audit record or outbox message.
/// </summary>
internal sealed class DealRecordAccessFactProvider(IDealsPersistence persistence) : IRecordAccessFactProvider
{
    /// <summary>
    /// Only capabilities behind an admitted Deals operation are declared. Deals has no export or
    /// approval operation, so neither is declared. <c>deals.delete</c> is the capability the admitted
    /// archive operation enforces, so it is declared as the delete capability rather than invented.
    /// </summary>
    private static readonly RecordAccessResourceDescriptor DealsDescriptor = RecordAccessResourceDescriptor.Create(
        resourceKey: DealAuthorization.ResourceKey,
        readCapability: DealCapabilities.Read.Capability,
        updateCapability: DealCapabilities.Update.Capability,
        deleteCapability: DealCapabilities.Delete.Capability,
        commandCapabilities: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["deal.create"] = DealCapabilities.Create.Capability,
            ["deal.update"] = DealCapabilities.Update.Capability,
            ["deal.move-stage"] = DealCapabilities.Update.Capability,
            ["deal.close-won"] = DealCapabilities.Close.Capability,
            ["deal.close-lost"] = DealCapabilities.Close.Capability,
            ["deal.delete"] = DealCapabilities.Delete.Capability
        },
        enforceableFields: DealFieldSecurity.EnforceableFields);

    public RecordAccessResourceDescriptor Descriptor => DealsDescriptor;

    public async Task<RecordAccessFacts> ReadFactsAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string recordId,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        if (!DealValidation.IsEntityId(recordId))
            return RecordAccessFacts.NotFound;

        var deal = await persistence.ReadDealAsync(trustedWorkspace.WorkspaceId, recordId, cancellationToken);
        return deal is null ? RecordAccessFacts.NotFound : DealAuthorization.Facts(deal);
    }
}
