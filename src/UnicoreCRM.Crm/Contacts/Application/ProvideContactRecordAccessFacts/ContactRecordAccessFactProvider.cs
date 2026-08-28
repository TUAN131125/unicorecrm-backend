using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.ProvideContactRecordAccessFacts;

internal sealed partial class ContactRecordAccessFactProvider(IContactsPersistence persistence) : IRecordAccessFactProvider
{
    private static readonly RecordAccessResourceDescriptor ContactsDescriptor = RecordAccessResourceDescriptor.Create(
        resourceKey: ContactAuthorization.ResourceKey,
        readCapability: ContactCapabilities.Read.Capability,
        enforceableFields: ContactFieldSecurity.EnforceableFields);

    public RecordAccessResourceDescriptor Descriptor => ContactsDescriptor;

    public async Task<RecordAccessFacts> ReadFactsAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string recordId,
        RecordAccessRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        if (!EntityIdPattern().IsMatch(recordId))
            return RecordAccessFacts.NotFound;

        var contact = await persistence.ReadContactAsync(
            trustedWorkspace.WorkspaceId,
            recordId,
            cancellationToken);
        return contact is null ? RecordAccessFacts.NotFound : ContactAuthorization.Facts(contact);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
