using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.ProvideLeadRecordAccessFacts;

/// <summary>
/// The Leads side of the narrow record-access fact boundary. Leads stays authoritative for Lead
/// existence and for the owner member reference record scope is defined against; AccessControl stays
/// authoritative for the decision. It authorizes nothing, queries only the trusted Workspace, and
/// writes no Lead state, audit record or outbox message.
/// </summary>
internal sealed class LeadRecordAccessFactProvider(ILeadsPersistence persistence) : IRecordAccessFactProvider
{
    /// <summary>
    /// Only capabilities behind an admitted Leads operation are declared. Leads has no delete,
    /// export, approval or assignment operation, so none is declared and none can ever be granted.
    /// The frontend also asks about merge, consent and archive commands; none has an admitted
    /// operation here, so none is declared.
    /// </summary>
    private static readonly RecordAccessResourceDescriptor LeadsDescriptor = RecordAccessResourceDescriptor.Create(
        resourceKey: LeadAuthorization.ResourceKey,
        readCapability: LeadCapabilities.Read.Capability,
        updateCapability: LeadCapabilities.Update.Capability,
        commandCapabilities: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["lead.create"] = LeadCapabilities.Create.Capability,
            ["lead.update"] = LeadCapabilities.Update.Capability,
            ["lead.change-work-state"] = LeadCapabilities.Update.Capability,
            ["lead.disqualify"] = LeadCapabilities.Qualify.Capability
        },
        enforceableFields: LeadFieldSecurity.EnforceableFields);

    public RecordAccessResourceDescriptor Descriptor => LeadsDescriptor;

    public async Task<RecordAccessFacts> ReadFactsAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string recordId,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        if (!LeadValidation.IsEntityId(recordId))
            return RecordAccessFacts.NotFound;

        var lead = await persistence.ReadLeadAsync(trustedWorkspace.WorkspaceId, recordId, cancellationToken);
        return lead is null ? RecordAccessFacts.NotFound : RecordAccessFacts.Found(lead.Profile.OwnerId);
    }
}
