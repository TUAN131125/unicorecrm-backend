using System.Text.RegularExpressions;
using UnicoreCRM.Billing.Payments.Application.Common;
using UnicoreCRM.Billing.Payments.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Billing.Payments.Application.ProvidePaymentRecordAccessFacts;

internal sealed partial class PaymentRecordAccessFactProvider(IPaymentsPersistence persistence) : IRecordAccessFactProvider
{
    private static readonly RecordAccessResourceDescriptor PaymentsDescriptor = RecordAccessResourceDescriptor.Create(
        resourceKey: PaymentAuthorization.ResourceKey,
        readCapability: PaymentCapabilities.Read.Capability,
        enforceableFields: PaymentFieldSecurity.EnforceableFields);

    public RecordAccessResourceDescriptor Descriptor => PaymentsDescriptor;

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
