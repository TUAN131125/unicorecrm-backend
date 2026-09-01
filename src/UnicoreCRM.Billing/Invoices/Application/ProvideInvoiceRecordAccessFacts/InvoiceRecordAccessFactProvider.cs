using System.Text.RegularExpressions;
using UnicoreCRM.Billing.Invoices.Application.Common;
using UnicoreCRM.Billing.Invoices.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Billing.Invoices.Application.ProvideInvoiceRecordAccessFacts;

/// <summary>
/// Declares the Invoices resource vocabulary to AccessControl and answers record-existence facts
/// from Invoices-owned, Workspace-scoped persistence only.
///
/// <para>No ownership fact is reported. Current authority defines no Invoice record owner or team:
/// buyer, order, payment-schedule and creation-intent identifiers are foreign scalar references,
/// not record ownership, so inferring an owner from them would widen access without provenance.
/// The owner member is therefore always null and OWN, TEAM and CUSTOM scopes fail closed.</para>
///
/// <para>Only read capability is declared. No Invoice mutation is implemented in this slice, so
/// update, delete, export, approve and command capabilities are left unset and deny.</para>
/// </summary>
internal sealed partial class InvoiceRecordAccessFactProvider(IInvoicesPersistence persistence) : IRecordAccessFactProvider
{
    private static readonly RecordAccessResourceDescriptor InvoicesDescriptor = RecordAccessResourceDescriptor.Create(
        resourceKey: InvoiceAuthorization.ResourceKey,
        readCapability: InvoiceCapabilities.Read.Capability,
        enforceableFields: InvoiceFieldSecurity.EnforceableFields);

    public RecordAccessResourceDescriptor Descriptor => InvoicesDescriptor;

    public async Task<RecordAccessFacts> ReadFactsAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string recordId,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        if (!EntityIdPattern().IsMatch(recordId)) return RecordAccessFacts.NotFound;
        return await persistence.RecordExistsAsync(trustedWorkspace.WorkspaceId, recordId, cancellationToken)
            ? RecordAccessFacts.Found(null)
            : RecordAccessFacts.NotFound;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
