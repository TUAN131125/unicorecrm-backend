using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Support.Application.ProvideSupportRecordAccessFacts;

/// <summary>
/// The Support side of the narrow record-access fact boundary. Support stays authoritative for
/// SupportCase existence, for the owner reference record scope is defined against, and for its own
/// capability and command vocabulary. AccessControl stays authoritative for the decision.
///
/// This provider deliberately does not authorize: AccessControl authorizes the caller before
/// calling it, and a second capability check here would duplicate the authorization authority and
/// the decision audit. It performs one read-only lookup already scoped to the trusted Workspace, so
/// a SupportCase belonging to another Workspace is reported as not found; it writes no SupportCase
/// state, no Support audit record and no outbox message, because an authorization evaluation is not
/// a Support business read.
/// </summary>
internal sealed class SupportRecordAccessFactProvider(ISupportPersistence persistence) : IRecordAccessFactProvider
{
    /// <summary>
    /// Only capabilities behind an admitted Support operation are declared. Support has no delete,
    /// export or approval operation, so those stay null and the matching actions are denied rather
    /// than being granted a capability name that nothing enforces. The transition commands map to
    /// <c>support.update</c> because <c>transitionSupportCase</c> is what enforces them.
    /// </summary>
    /// <remarks>
    /// Static because the descriptor is a constant: building it validates every capability
    /// identifier, and the provider is resolved once per request.
    /// </remarks>
    private static readonly RecordAccessResourceDescriptor SupportDescriptor = RecordAccessResourceDescriptor.Create(
        resourceKey: "support",
        readCapability: SupportCapabilities.Read.Capability,
        updateCapability: SupportCapabilities.Update.Capability,
        commandCapabilities: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["support.create"] = SupportCapabilities.Create.Capability,
            ["support.update"] = SupportCapabilities.Update.Capability,
            ["support.assign"] = SupportCapabilities.Assign.Capability,
            ["support.resolve"] = SupportCapabilities.Update.Capability,
            ["support.close"] = SupportCapabilities.Update.Capability,
            ["support.reopen"] = SupportCapabilities.Update.Capability,
            ["support.cancel"] = SupportCapabilities.Update.Capability
        });

    public RecordAccessResourceDescriptor Descriptor => SupportDescriptor;

    public async Task<RecordAccessFacts> ReadFactsAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string recordId,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        if (!SupportValidation.IsEntityId(recordId))
            return RecordAccessFacts.NotFound;

        var supportCase = await persistence.ReadCaseAsync(trustedWorkspace.WorkspaceId, recordId, cancellationToken);
        return supportCase is null
            ? RecordAccessFacts.NotFound
            : RecordAccessFacts.Found(supportCase.OwnerId);
    }
}
