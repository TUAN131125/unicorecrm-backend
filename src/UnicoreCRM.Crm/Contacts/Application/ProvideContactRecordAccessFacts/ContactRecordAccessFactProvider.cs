using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.ProvideContactRecordAccessFacts;

internal sealed partial class ContactRecordAccessFactProvider(IContactsPersistence persistence) : IActivityReferenceAuthority
{
    public string ReadFactsSql => persistence.ActivityReadFactsSql;

    public async Task<bool> IsActivityReferenceValidAsync(TrustedWorkspaceContext workspace, string recordId,
        BuyerReference? relationship, bool forWrite, CancellationToken cancellationToken)
    {
        var contact = await persistence.ReadContactAsync(workspace.WorkspaceId, recordId, cancellationToken);
        if (contact is null || (forWrite && (contact.ArchivedAt is not null || contact.Status == "archived"))) return false;
        if (relationship is null) return true;
        if (relationship.Type == "CONTACT") return relationship.Id == recordId;
        if (relationship.Type != "ORGANIZATION_ACCOUNT" || relationship.Id is null) return false;
        if (forWrite)
            return await persistence.HasActiveOrganizationRelationshipAsync(workspace.WorkspaceId, recordId, relationship.Id, cancellationToken);
        // Ending an affiliation prevents new writes; it does not erase a valid past
        // interaction. Current access to both records is still required by Tasks.
        var history = await persistence.ReadOrganizationRelationshipsAsync(workspace.WorkspaceId, recordId, cancellationToken);
        return history.Any(item => item.OrganizationId == relationship.Id);
    }

    private static readonly RecordAccessResourceDescriptor ContactsDescriptor = RecordAccessResourceDescriptor.Create(
        resourceKey: ContactAuthorization.ResourceKey,
        readCapability: ContactCapabilities.Read.Capability,
        updateCapability: ContactCapabilities.Update.Capability,
        deleteCapability: ContactCapabilities.Archive.Capability,
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
